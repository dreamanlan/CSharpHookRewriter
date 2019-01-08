project("PluginFramework")
{
	InjectMemoryLog();
	ExcludeAssembly("System\\..*");
	
	DontInject("__Error.*");
	DontInject("CsLibrary\\.Logger.*");
	DontInject("CsLibrary\\.GameLogger.*");
	DontInject("CsLibrary\\.QSLogger.*");
	DontInject("Utility\\.GfxLog");
};

project("Cs2LuaScript")
{
	InjectMemoryLog();
	ExcludeAssembly("System\\..*");
};