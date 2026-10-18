// This is an inert installer migration fixture, never a distributable manager.
// Migration tests inspect/replace its files; they must not execute it.
System.Console.Error.WriteLine("Installer test fixture; no application implementation.");
return 99;
