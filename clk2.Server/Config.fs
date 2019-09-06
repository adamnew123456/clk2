module Config
open System
open System.IO

/// <summary>
/// The path where the ClockStore database is located
/// </summary>
let store_location =
    let base_dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
    Path.Combine(base_dir, "clk2.json")

/// <summary>
/// How many days to prune the event database on startup
/// </summary>
let prune_days = 30.0

/// <summary>
/// The port to bind the RPC server at
/// </summary>
let port = 6996
