// Test-owned process used to exercise the installation-specific running guard.
File.WriteAllText(args.Single(), "ready");
Thread.Sleep(Timeout.Infinite);
