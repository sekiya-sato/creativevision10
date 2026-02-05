[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
/*
 MSTestSettings.cs はアセンブリ単位で Parallelize(Scope = ExecutionScope.MethodLevel) 属性を付与し、
MSTest ランナーがテスト メソッドを並列実行できるようにするためのフラグです。
削除すると全テストが逐次実行に戻り、実行時間は延びる一方、共有リソース競合の心配は減ります。
テストを安全に並列化したいなら残し、テスト間で共有状態が発生しやすくレースが懸念されるならコメント化して逐次実行に戻してください。
 */