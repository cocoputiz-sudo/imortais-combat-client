#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais.Highlights;

internal sealed class HighlightStorageManager
{
    public const long DefaultQuotaBytes=10L*1024*1024*1024;
    public const long MinimumFreeBytes=1L*1024*1024*1024;
    public const long DefaultReservationBytes=256L*1024*1024;

    private readonly object _gate=new();
    private readonly string _folder;
    private long _usedBytes;
    private bool _initialized;

    public HighlightStorageManager()
    {
        var videos=Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        _folder=Path.Combine(videos,"IMORTAIS Highlights");
    }

    public string Folder=>_folder;
    public long QuotaBytes=>DefaultQuotaBytes;
    public long UsedBytes{get{lock(_gate)return _usedBytes;}}

    public void Initialize()
    {
        lock(_gate)
        {
            Directory.CreateDirectory(_folder);
            foreach(var partial in Directory.EnumerateFiles(_folder,"*.partial.mp4",SearchOption.AllDirectories))
            {
                try{File.Delete(partial);}catch{}
            }
            _usedBytes=CalculateUsedBytes();
            _initialized=true;
        }
    }

    public void PrepareForSave(long reserveBytes=DefaultReservationBytes)
    {
        lock(_gate)
        {
            EnsureInitialized();
            EnforceQuota(reserveBytes,null);
            var root=Path.GetPathRoot(Path.GetFullPath(_folder));
            if(string.IsNullOrWhiteSpace(root))throw new IOException("Não foi possível identificar a unidade de destino dos highlights.");
            var drive=new DriveInfo(root);
            var requiredFree=checked(MinimumFreeBytes+Math.Max(0,reserveBytes));
            if(drive.AvailableFreeSpace<requiredFree)
                throw new IOException($"Espaço livre insuficiente para preservar 1 GB após o save ({drive.AvailableFreeSpace/1024d/1024d/1024d:0.00} GB livres). Salvamento de highlight suspenso.");
        }
    }

    public string CreateAutomaticPath(HighlightClipPlan plan,DateTime firstEventLocalTime)
    {
        lock(_gate)
        {
            EnsureInitialized();
            var destination=plan.SelectedTrigger.Kind==HighlightTriggerKind.Death
                ?Path.Combine(_folder,"Mortes")
                :Path.Combine(_folder,plan.MassAbate?"Abates em Massa":"Abates");
            Directory.CreateDirectory(destination);
            return EnsureUniquePath(HighlightFileNaming.BuildFileName(plan.SelectedTrigger,plan.TriggerCount,firstEventLocalTime),destination);
        }
    }

    public string CreateManualPath()
    {
        lock(_gate)
        {
            EnsureInitialized();
            return EnsureUniquePath($"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_TESTE.mp4");
        }
    }

    public void CompleteSave(string finalPath)
    {
        lock(_gate)
        {
            EnsureInitialized();
            EnforceQuota(0,finalPath);
            _usedBytes=CalculateUsedBytes();
        }
    }

    public void RefreshUsage()
    {
        lock(_gate)
        {
            EnsureInitialized();
            _usedBytes=CalculateUsedBytes();
        }
    }

    private void EnforceQuota(long reserveBytes,string? protectedPath)
    {
        var files=Directory.EnumerateFiles(_folder,"*.mp4",SearchOption.AllDirectories)
            .Where(path=>!path.EndsWith(".partial.mp4",StringComparison.OrdinalIgnoreCase))
            .Select(path=>
            {
                var info=new FileInfo(path);
                return new HighlightQuotaFile(info.FullName,info.Length,info.LastWriteTimeUtc);
            })
            .ToArray();

        foreach(var path in HighlightQuotaPlanner.SelectFilesToDelete(files,DefaultQuotaBytes,reserveBytes,protectedPath))
        {
            try{File.Delete(path);}catch{}
        }
        _usedBytes=CalculateUsedBytes();
    }

    private long CalculateUsedBytes()
    {
        long total=0;
        if(!Directory.Exists(_folder))return 0;
        foreach(var path in Directory.EnumerateFiles(_folder,"*.mp4",SearchOption.TopDirectoryOnly))
        {
            if(path.EndsWith(".partial.mp4",StringComparison.OrdinalIgnoreCase))continue;
            try{total=checked(total+new FileInfo(path).Length);}catch{}
        }
        return total;
    }

    private string EnsureUniquePath(string fileName,string? directory=null)
    {
        var folder=directory??_folder;
        var path=Path.Combine(folder,fileName);
        if(!File.Exists(path)&&!File.Exists(path+".partial.mp4"))return path;

        var stem=Path.GetFileNameWithoutExtension(fileName);
        for(var i=2;i<10_000;i++)
        {
            path=Path.Combine(folder,$"{stem}_{i}.mp4");
            if(!File.Exists(path)&&!File.Exists(path+".partial.mp4"))return path;
        }
        throw new IOException("Não foi possível gerar um nome único para o highlight.");
    }

    private void EnsureInitialized()
    {
        if(!_initialized)Initialize();
    }
}
