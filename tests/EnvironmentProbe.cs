using System;
using System.IO;
using System.Threading;
class EnvironmentProbe {
    static void Main() {
        Thread.Sleep(1000);
        File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "child-result.txt"), new [] {
            Environment.GetEnvironmentVariable("HTTP_PROXY"),
            Environment.GetEnvironmentVariable("HTTPS_PROXY"),
            Environment.GetEnvironmentVariable("NO_PROXY")
        });
    }
}