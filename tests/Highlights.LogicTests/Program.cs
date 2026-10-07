using StatisticsAnalysisTool.Imortais.Highlights;

static void Eq<T>(T actual,T expected,string message)
{
    if(!EqualityComparer<T>.Default.Equals(actual,expected))
        throw new InvalidOperationException($"{message}: esperado={expected}, atual={actual}");
}
static void True(bool value,string message){if(!value)throw new InvalidOperationException(message);}
static byte[] Bytes(int size,byte seed){var a=new byte[size];for(var i=0;i<a.Length;i++)a[i]=(byte)(seed+i);return a;}

static void TestEvictionByTime()
{
    using var ring=new EncodedSampleRing(TimeSpan.FromSeconds(150),1024,64);
    ring.TryWrite(Bytes(10,1),0,TimeSpan.FromSeconds(1).Ticks,true);
    ring.TryWrite(Bytes(10,2),TimeSpan.FromSeconds(100).Ticks,TimeSpan.FromSeconds(1).Ticks,false);
    ring.TryWrite(Bytes(10,3),TimeSpan.FromSeconds(151.1).Ticks,TimeSpan.FromSeconds(1).Ticks,false);
    Eq(ring.Count,2,"amostra fora da janela de 150 s deve sair");
    Eq(ring.Describe()[0].Timestamp100Ns,TimeSpan.FromSeconds(100).Ticks,"a amostra de 100 s deve ser a mais antiga");
    Console.WriteLine("✅ ring descarta por tempo");
}

static void TestEvictionByBytes()
{
    using var ring=new EncodedSampleRing(TimeSpan.FromMinutes(10),25,64);
    ring.TryWrite(Bytes(10,1),0,1,true);
    ring.TryWrite(Bytes(10,2),10,1,false);
    ring.TryWrite(Bytes(10,3),20,1,false);
    Eq(ring.Count,2,"capacidade de bytes deve expulsar amostra mais antiga");
    Eq(ring.BytesUsed,20L,"bytes usados devem respeitar teto");
    Eq(ring.Describe()[0].Timestamp100Ns,10L,"segunda amostra deve virar a mais antiga");
    Console.WriteLine("✅ ring descarta por bytes");
}

static void TestCleanPointAndRebase()
{
    using var ring=new EncodedSampleRing(TimeSpan.FromMinutes(10),4096,64);
    ring.TryWrite(Bytes(5,1),100,10,true);
    ring.TryWrite(Bytes(6,2),200,10,false);
    ring.TryWrite(Bytes(7,3),300,10,true);
    ring.TryWrite(Bytes(8,4),400,10,false);
    ring.TryWrite(Bytes(9,5),500,10,false);

    using var snap=ring.CreateSnapshot(350,500);
    True(snap is not null,"snapshot deve existir");
    Eq(snap!.Count,3,"deve iniciar no clean point de 300");
    Eq(snap.Samples[0].Timestamp100Ns,0L,"primeiro timestamp deve ser rebased para zero");
    Eq(snap.Samples[1].Timestamp100Ns,100L,"segundo timestamp deve preservar delta");
    True(snap.Samples[0].IsCleanPoint,"primeira amostra precisa ser clean point");
    Eq(snap.GetBytes(0)[0],(byte)3,"payload do snapshot deve vir do clean point correto");
    Console.WriteLine("✅ snapshot escolhe CleanPoint anterior e rebasa timestamps");
}

static void TestNoCleanPointFailsCleanly()
{
    using var ring=new EncodedSampleRing(TimeSpan.FromMinutes(10),1024,64);
    ring.TryWrite(Bytes(10,1),100,10,false);
    ring.TryWrite(Bytes(10,2),200,10,false);
    var snap=ring.CreateSnapshot(250,300);
    True(snap is null,"sem clean point anterior o snapshot deve falhar de forma limpa");
    Console.WriteLine("✅ snapshot falha limpo sem CleanPoint");
}

TestEvictionByTime();
TestEvictionByBytes();
TestCleanPointAndRebase();
TestNoCleanPointFailsCleanly();

static void TestFramePacer60Jitter2ms()
{
    var pacer=new FramePacer(60);
    var accepted=0;
    long lastTimestamp=-1;
    var baseStep=TimeSpan.TicksPerSecond/60d;

    for(var i=0;i<600;i++)
    {
        var jitterMs=(i%3-1)*2.0; // -2 / 0 / +2 ms
        var source=(long)Math.Round(i*baseStep+jitterMs*TimeSpan.TicksPerMillisecond);
        if(!pacer.TryAccept(source,out var encoded,out var duration))continue;
        accepted++;
        True(encoded>lastTimestamp,"timestamps codificados devem ser estritamente crescentes");
        True(duration>0,"duração codificada deve ser positiva");
        lastTimestamp=encoded;
    }

    True(accepted>=594,$"60 Hz com jitter ±2 ms deve aceitar >=99%; aceitos={accepted}/600");
    Console.WriteLine($"✅ frame pacer 60 Hz ±2 ms aceita {accepted}/600 (>=99%)");
}

static void TestFramePacer144To60()
{
    var pacer=new FramePacer(60);
    var accepted=0;
    var baseStep=TimeSpan.TicksPerSecond/144d;

    for(var i=0;i<1440;i++)
    {
        var source=(long)Math.Round(i*baseStep);
        if(pacer.TryAccept(source,out _,out _))accepted++;
    }

    True(accepted>=590&&accepted<=610,$"fonte 144 Hz deve ser limitada para ~60 FPS; aceitos={accepted}");
    Console.WriteLine($"✅ frame pacer limita 144 Hz para ~60 FPS ({accepted} frames em ~10 s)");
}

static void TestFramePacer57Irregular()
{
    var pacer=new FramePacer(60);
    var accepted=0;
    var baseStep=TimeSpan.TicksPerSecond/57d;

    for(var i=0;i<570;i++)
    {
        var jitterMs=(i%3-1)*4.0; // -4 / 0 / +4 ms
        var source=(long)Math.Round(i*baseStep+jitterMs*TimeSpan.TicksPerMillisecond);
        if(pacer.TryAccept(source,out _,out _))accepted++;
    }

    True(accepted>=564,$"fonte ~57 FPS irregular deve aceitar >=99%; aceitos={accepted}/570");
    Console.WriteLine($"✅ frame pacer ~57 FPS irregular aceita {accepted}/570 (>=99%)");
}

static void TestFramePacer30From60Hz()
{
    var pacer=new FramePacer(30);
    var accepted=0;
    var baseStep=TimeSpan.TicksPerSecond/60d;

    for(var i=0;i<600;i++)
    {
        var jitterMs=(i%3-1)*2.0;
        var source=(long)Math.Round(i*baseStep+jitterMs*TimeSpan.TicksPerMillisecond);
        if(pacer.TryAccept(source,out _,out _))accepted++;
    }

    True(accepted>=295&&accepted<=305,$"30 FPS sobre fonte 60 Hz deve ficar próximo de 300/600; aceitos={accepted}");
    Console.WriteLine($"✅ frame pacer 30 FPS seleciona {accepted}/600");
}

static void TestAudioRing()
{
    using var ring=new PcmAudioRing(TimeSpan.FromSeconds(2),64,48_000,2,16,32);
    var packet=Bytes(16,10); // 4 frames PCM stereo 16-bit
    ring.TryWrite(packet,0);
    ring.TryWrite(packet,TimeSpan.FromSeconds(1).Ticks);
    ring.TryWrite(packet,TimeSpan.FromSeconds(2.1).Ticks);
    True(ring.Count<=2,"ring PCM deve descartar pacote fora da janela temporal");

    using var snap=ring.CreateSnapshot(
        TimeSpan.FromSeconds(1).Ticks,
        TimeSpan.FromSeconds(3).Ticks,
        TimeSpan.FromSeconds(1).Ticks);
    True(snap is not null&&snap.Count>=1,"snapshot PCM deve existir");
    Eq(snap!.Samples[0].Timestamp100Ns,0L,"áudio deve ser rebased ao início do clipe");
    Console.WriteLine("✅ ring PCM descarta por tempo e rebasa timestamps");
}

static byte[] SnapshotBytes(PcmAudioRing.AudioSnapshot snapshot)
{
    var bytes=new byte[snapshot.Length];
    var offset=0;
    for(var i=0;i<snapshot.Count;i++)
    {
        var part=snapshot.GetBytes(i);
        part.CopyTo(bytes.AsSpan(offset));
        offset+=part.Length;
    }
    Eq(offset,snapshot.Length,"snapshot PCM deve preencher todo o buffer");
    return bytes;
}

static void TestAudioJitterTolerance()
{
    const int packetFrames=10;
    const int packetBytes=packetFrames*2; // mono PCM16 @ 1 kHz => 10 ms
    const int packetCount=500;
    var anchor=TimeSpan.FromSeconds(1).Ticks;
    using var ring=new PcmAudioRing(TimeSpan.FromSeconds(10),32_768,1000,1,16,1024);
    var expected=new byte[packetBytes*packetCount];

    for(var i=0;i<packetCount;i++)
    {
        var packet=new byte[packetBytes];
        for(var j=0;j<packet.Length;j++)packet[j]=(byte)((i*17+j)%251+1);
        packet.CopyTo(expected,i*packetBytes);

        var jitter=i==0?0:(i%3-1)*(TimeSpan.TicksPerMillisecond/2); // -0,5 / 0 / +0,5 ms
        var timestamp=anchor+i*TimeSpan.FromMilliseconds(10).Ticks+jitter;
        True(ring.TryWrite(packet,timestamp),"pacote com jitter deve entrar no ring");
    }

    using var snap=ring.CreateSnapshot(
        anchor,
        anchor+TimeSpan.FromMilliseconds(packetCount*10).Ticks,
        anchor);
    True(snap is not null,"snapshot PCM com jitter deve existir");
    Eq(ring.GapsDetected,0L,"jitter de ±0,5 ms não pode virar lacuna");
    Eq(snap!.GapCount,0,"snapshot não deve inserir silêncio para jitter normal");
    True(SnapshotBytes(snap).SequenceEqual(expected),"PCM com jitter deve ser idêntico à concatenação dos pacotes");
    Console.WriteLine("✅ 500 pacotes PCM com jitter ±0,5 ms permanecem contínuos e sem lacunas");
}

static void TestAudioRealGap()
{
    const int packetBytes=20; // 10 ms mono PCM16 @ 1 kHz
    using var ring=new PcmAudioRing(TimeSpan.FromSeconds(2),4096,1000,1,16,64);
    var first=Enumerable.Repeat((byte)0x11,packetBytes).ToArray();
    var second=Enumerable.Repeat((byte)0x22,packetBytes).ToArray();

    ring.TryWrite(first,0);
    ring.TryWrite(second,TimeSpan.FromMilliseconds(210).Ticks); // esperado 10 ms: salto real de 200 ms

    using var snap=ring.CreateSnapshot(0,TimeSpan.FromMilliseconds(220).Ticks,0);
    True(snap is not null,"snapshot com salto real deve existir");
    Eq(ring.GapsDetected,1L,"salto de 200 ms deve contar exatamente uma lacuna");
    Eq(snap!.GapCount,1,"snapshot deve inserir exatamente uma lacuna");
    var bytes=SnapshotBytes(snap);
    True(bytes.AsSpan(0,packetBytes).SequenceEqual(first),"primeiro pacote deve ser preservado");
    for(var i=packetBytes;i<packetBytes+400;i++)Eq(bytes[i],(byte)0,"200 ms devem virar silêncio PCM");
    True(bytes.AsSpan(packetBytes+400,packetBytes).SequenceEqual(second),"segundo pacote deve vir após o silêncio");
    Console.WriteLine("✅ salto PCM de 200 ms gera exatamente uma lacuna de silêncio");
}

static void TestAudioBoundsFromVideoEpoch()
{
    var bounds=HighlightTimeline.AudioClipFromVideoEpoch(1_000_000,200,900);
    Eq(bounds.Start100Ns,1_000_200L,"início absoluto de áudio deve usar epoch do vídeo");
    Eq(bounds.End100Ns,1_000_900L,"fim absoluto de áudio deve usar epoch do vídeo");
    Console.WriteLine("✅ limites de áudio são derivados do epoch do vídeo");
}

static void TestNv12PoolStallWatchdog()
{
    var watchdog=new Nv12PoolStallWatchdog(TimeSpan.FromSeconds(2));
    True(!watchdog.Observe(true,0),"primeiro pool cheio apenas inicia o relógio");
    True(!watchdog.Observe(true,TimeSpan.FromMilliseconds(1999).Ticks),"antes de 2 s não reinicia");
    True(watchdog.Observe(true,TimeSpan.FromSeconds(2).Ticks),"2 s de pool cheio devem pedir reinício");
    True(!watchdog.Observe(false,TimeSpan.FromSeconds(2.1).Ticks),"pool liberado deve resetar watchdog");
    True(!watchdog.Observe(true,TimeSpan.FromSeconds(3).Ticks),"novo episódio cheio reinicia a contagem");
    Console.WriteLine("✅ watchdog NV12 só dispara após 2 s contínuos de pool cheio");
}

static void TestTextureLeasePool()
{
    var pool=new TextureLeasePool(2);
    True(pool.TryAcquire(out var first),"primeira textura deve estar livre");
    True(pool.TryAcquire(out var second),"segunda textura deve estar livre");
    True(first!=second,"duas leases simultâneas não podem apontar para a mesma textura");
    True(!pool.TryAcquire(out _),"pool cheio não pode reutilizar textura ainda alugada");
    pool.Release(first);
    True(pool.TryAcquire(out var afterRelease),"slot liberado deve voltar a ficar disponível");
    Eq(afterRelease,first,"somente o slot explicitamente liberado pode ser reutilizado");
    pool.Release(second);
    pool.Release(afterRelease);
    Console.WriteLine("✅ pool NV12 não reutiliza textura antes da liberação");
}


static HighlightTrigger Trigger(HighlightTriggerKind kind,long seconds,string victim="Vitima",string killer="Matador")
    =>new(kind,victim,killer,TimeSpan.FromSeconds(seconds).Ticks);

static void TestHighlightCoalescence()
{
    var one=new HighlightClipCoalescer(TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(120));
    True(!one.Add(Trigger(HighlightTriggerKind.Abate,100)),"primeiro evento cria clipe");
    Eq(one.Count,1,"um evento deve gerar um clipe");
    True(one.TryDequeueReady(TimeSpan.FromSeconds(115).Ticks,out var onePlan),"clipe único deve ficar pronto após pós-roll");
    Eq(onePlan!.TriggerCount,1,"clipe único deve conter um gatilho");

    var three=new HighlightClipCoalescer(TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(120));
    three.Add(Trigger(HighlightTriggerKind.Abate,100));
    True(three.Add(Trigger(HighlightTriggerKind.Abate,105)),"evento em +5 s deve coalescer");
    True(three.Add(Trigger(HighlightTriggerKind.Abate,110)),"evento em +10 s deve coalescer");
    Eq(three.Count,1,"três eventos em 10 s devem formar um clipe");
    True(three.TryDequeueReady(TimeSpan.FromSeconds(125).Ticks,out var threePlan),"clipe coalescido deve terminar 15 s após último evento");
    Eq(threePlan!.TriggerCount,3,"clipe deve registrar três gatilhos");

    var separated=new HighlightClipCoalescer(TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(120));
    separated.Add(Trigger(HighlightTriggerKind.Abate,100));
    True(!separated.Add(Trigger(HighlightTriggerKind.Abate,140)),"evento 40 s depois deve abrir outro clipe");
    Eq(separated.Count,2,"eventos separados por 40 s devem gerar dois clipes");

    var capped=new HighlightClipCoalescer(TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(120));
    for(var s=100;s<=170;s+=10)capped.Add(Trigger(HighlightTriggerKind.Abate,s));
    True(capped.TryDequeueReady(TimeSpan.FromSeconds(175).Ticks,out var cappedPlan),"primeiro clipe deve atingir teto");
    Eq(cappedPlan!.PlannedDuration,TimeSpan.FromSeconds(120),"clipe coalescido nunca pode exceder 120 s");
    Console.WriteLine("✅ coalescência Marco 2: único, 3/10s, separação 40s e teto 120s");
}

static void TestPersonalMassKillBurstCoalescence()
{
    var burst=new HighlightClipCoalescer(TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(120));
    for(var i=0;i<20;i++)
    {
        var tenthSeconds=i%20; // 20 abates distribuídos em apenas 1,9 s
        var trigger=new HighlightTrigger(
            HighlightTriggerKind.Abate,
            $"Enemy{i+1}",
            "BadMack",
            TimeSpan.FromSeconds(100).Ticks+TimeSpan.FromMilliseconds(tenthSeconds*100).Ticks);
        burst.Add(trigger);
    }

    Eq(burst.Count,1,"mass kill pessoal curta não pode criar vários clipes");
    True(burst.TryDequeueReady(TimeSpan.FromSeconds(117).Ticks,out var plan),"burst deve finalizar 15 s após o último abate");
    Eq(plan!.TriggerCount,20,"os 20 abates devem pertencer ao mesmo clipe");
    Console.WriteLine("✅ mass kill pessoal: 20 abates em 1,9 s => 1 clipe x20");
}

static void TestBoundedAdmissionCounter()
{
    var gate=new BoundedAdmissionCounter(2);
    True(gate.TryEnter(),"slot 1 deve entrar");
    True(gate.TryEnter(),"slot 2 deve entrar");
    True(!gate.TryEnter(),"capacidade não pode ser excedida");
    Eq(gate.Count,2,"contador deve parar na capacidade");
    Eq(gate.Peak,2,"pico deve registrar capacidade atingida");
    gate.Exit();
    True(gate.TryEnter(),"slot liberado deve ser reutilizável");
    gate.Exit();
    gate.Exit();
    Eq(gate.Count,0,"todos os slots devem ser liberados");
    Console.WriteLine("✅ admissão limitada impede crescimento ilimitado");
}

static void TestTriggerDeduplication()
{
    var dedup=new HighlightTriggerDeduplicator(TimeSpan.FromSeconds(1));
    var first=new HighlightTrigger(HighlightTriggerKind.Abate,"Enemy","BadMack",TimeSpan.FromSeconds(10).Ticks);
    var duplicate=first with { Qpc100Ns=TimeSpan.FromSeconds(10.5).Ticks };
    var later=first with { Qpc100Ns=TimeSpan.FromSeconds(12).Ticks };
    True(!dedup.IsDuplicate(first),"primeiro abate não é duplicado");
    True(dedup.IsDuplicate(duplicate),"mesmo abate em 500 ms deve ser deduplicado");
    True(!dedup.IsDuplicate(later),"mesmo nome após janela deve voltar a ser aceito");
    Console.WriteLine("✅ dedupe evita DiedEvent repetido sem engolir abate posterior");
}

static void TestQpcToVideoTimeline()
{
    Eq(HighlightTimeline.QpcToVideoTimestamp100Ns(9_000_000,2_500_000),6_500_000L,"QPC deve ser convertido por subtração do epoch WGC");
    Console.WriteLine("✅ QPC converte para timeline de vídeo pelo epoch WGC");
}

static void TestHighlightQuotaSelection()
{
    var now=DateTime.UtcNow;
    var files=new[]
    {
        new HighlightQuotaFile("old.mp4",30,now.AddMinutes(-3)),
        new HighlightQuotaFile("mid.mp4",30,now.AddMinutes(-2)),
        new HighlightQuotaFile("new.mp4",30,now.AddMinutes(-1))
    };
    var deleted=HighlightQuotaPlanner.SelectFilesToDelete(files,100,30);
    Eq(deleted.Count,1,"quota deve remover só o necessário");
    Eq(deleted[0],"old.mp4","quota deve apagar primeiro o MP4 mais antigo");

    var protectedDelete=HighlightQuotaPlanner.SelectFilesToDelete(files,40,0,"old.mp4");
    True(!protectedDelete.Contains("old.mp4"),"arquivo protegido nunca pode ser selecionado");
    Console.WriteLine("✅ quota seleciona MP4s mais antigos e respeita arquivo protegido");
}

static void TestHighlightFileNaming()
{
    Eq(HighlightFileNaming.SanitizeComponent("A<B>:C?* /"),"A_B__C____","nome deve sanear caracteres inválidos");
    var name=HighlightFileNaming.BuildFileName(
        new HighlightTrigger(HighlightTriggerKind.Abate,"Enemy/Name","BadMack",1),
        3,
        new DateTime(2026,10,7,1,2,3));
    Eq(name,"2026-10-07_01-02-03_ABATE_Enemy_Name_x3.mp4","nome coalescido deve usar primeiro abate e contagem");
    Console.WriteLine("✅ nomes de highlight são saneados e incluem contagem coalescida");
}

TestHighlightCoalescence();
TestPersonalMassKillBurstCoalescence();
TestBoundedAdmissionCounter();
TestTriggerDeduplication();
TestQpcToVideoTimeline();
TestHighlightQuotaSelection();
TestHighlightFileNaming();
TestFramePacer60Jitter2ms();
TestFramePacer144To60();
TestFramePacer57Irregular();
TestFramePacer30From60Hz();
TestAudioRing();
TestAudioJitterTolerance();
TestAudioRealGap();
TestAudioBoundsFromVideoEpoch();
TestNv12PoolStallWatchdog();
TestTextureLeasePool();
Console.WriteLine("\n✅ Highlights logic suite: TODOS OS TESTES PASSARAM");
