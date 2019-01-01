project("PluginFramework")
{
	InjectMemoryLog();
	
	DontInject("__Error.*");
	DontInject("CsLibrary\\.Logger.*");
	DontInject("CsLibrary\\.GameLogger.*");
	DontInject("CsLibrary\\.QSLogger.*");
	DontInject("Utility\\.GfxLog");
};

project("Cs2LuaScript")
{
	InjectMemoryLog();
};