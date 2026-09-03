using NUnit.Framework;

// v3.82: a suíte de Integration compartilha um único banco por execução. O runner é serializado para
// impedir concorrência acidental entre fixtures. Testes que validam locks/concorrência continuam
// livres para abrir múltiplas SqlConnections/Tasks deliberadamente dentro do próprio teste.
[assembly: LevelOfParallelism(1)]
