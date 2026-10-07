#nullable enable
using System;

namespace StatisticsAnalysisTool.Imortais.Highlights;

/// <summary>
/// Controla a cadência sem quantizar timestamps para uma grade CFR.
/// Fontes no alvo ou abaixo dele preservam praticamente todos os frames.
/// Fontes acima do alvo são limitadas pela contagem temporal, mas os timestamps
/// entregues ao encoder continuam sendo os timestamps reais relativos ao epoch.
/// </summary>
internal sealed class FramePacer
{
    private readonly long _frameDuration100Ns;
    private readonly long _minimumAcceptInterval100Ns;
    private long? _epoch100Ns;
    private long? _lastAcceptedSource100Ns;
    private long _lastEncodedTimestamp100Ns=-1;
    private long _acceptedCount;

    public FramePacer(int fps)
    {
        if(fps is not (30 or 60))throw new ArgumentOutOfRangeException(nameof(fps));
        _frameDuration100Ns=TimeSpan.TicksPerSecond/fps;
        _minimumAcceptInterval100Ns=Math.Max(1,_frameDuration100Ns/2);
    }

    public long FrameDuration100Ns=>_frameDuration100Ns;
    public long MinimumAcceptInterval100Ns=>_minimumAcceptInterval100Ns;

    public bool TryAccept(
        long sourceTimestamp100Ns,
        out long encodedTimestamp100Ns,
        out long encodedDuration100Ns)
    {
        encodedTimestamp100Ns=0;
        encodedDuration100Ns=_frameDuration100Ns;

        if(_epoch100Ns is null)
        {
            _epoch100Ns=sourceTimestamp100Ns;
            _lastAcceptedSource100Ns=sourceTimestamp100Ns;
            _lastEncodedTimestamp100Ns=0;
            _acceptedCount=1;
            return true;
        }

        var lastSource=_lastAcceptedSource100Ns!.Value;
        var interval=sourceTimestamp100Ns-lastSource;

        // Regra principal: um frame novo só é candidato quando está pelo menos
        // a meia duração do frame-alvo após o último frame aceito.
        if(interval<_minimumAcceptInterval100Ns)return false;

        var elapsed=sourceTimestamp100Ns-_epoch100Ns.Value;
        if(elapsed<=_lastEncodedTimestamp100Ns)return false;

        // Não deixamos uma fonte muito acima do alvo (ex.: 144 Hz) produzir mais
        // que aproximadamente o FPS configurado. Isso é apenas um teto de contagem;
        // não altera o timestamp real do frame nem o encaixa em uma grade.
        var maxAcceptedByNow=checked(
            (elapsed+_minimumAcceptInterval100Ns)/_frameDuration100Ns+1);
        if(_acceptedCount>=maxAcceptedByNow)return false;

        encodedTimestamp100Ns=elapsed;
        encodedDuration100Ns=Math.Max(1,elapsed-_lastEncodedTimestamp100Ns);

        _lastAcceptedSource100Ns=sourceTimestamp100Ns;
        _lastEncodedTimestamp100Ns=encodedTimestamp100Ns;
        _acceptedCount++;
        return true;
    }

    public bool TryAccept(long sourceTimestamp100Ns,out long encodedTimestamp100Ns)
        =>TryAccept(sourceTimestamp100Ns,out encodedTimestamp100Ns,out _);
}
