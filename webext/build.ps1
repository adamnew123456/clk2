Try {
    New-Item -ItemType Directory -Name build
} Catch {
    # PS errors out if the item already exists
}

Remove-Item build/*

$files=@("manifest.json", "options.html", "options.js", "sidebar.css", "sidebar.html", "sidebar.js")
Compress-Archive -Path $files -CompressionLevel NoCompression -DestinationPath build/clk2@kidneybone.com.zip

Rename-Item build/clk2@kidneybone.com.zip build/clk2@kidneybone.com.xpi
