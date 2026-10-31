using StatisticsAnalysisTool.Imortais;
if(CombatTargetClassifier.Classify(true,false)!="player")throw new Exception("Player");
if(CombatTargetClassifier.Classify(true,true)!="player")throw new Exception("Player precedence");
if(CombatTargetClassifier.Classify(false,true)!="mob")throw new Exception("Confirmed mob");
if(CombatTargetClassifier.Classify(false,false)!="unknown")throw new Exception("Unknown");
Console.WriteLine("PASS target classification: player / confirmed mob / unknown");
