// 占位文件。
//
// 这组 Scene Query 测试依赖 Crowd VAT runtime 类型，但当前仓库的
// `Assets/Tests/EditMode/NewTree.EditMode.Tests.asmdef` 不能直接引用
// `Assembly-CSharp` 中的 crowd runtime 脚本。
//
// 如果把原测试代码保留在这里，会导致整个 Unity 编译链被测试程序集卡住，
// 进而无法进入 Play Mode。
//
// 后续若要恢复这些测试，需要先给 Crowd VAT runtime 提供可被测试程序集引用的
// 独立 asmdef，或者把测试迁移到不会落入 `NewTree.EditMode.Tests` 的编译单元。
