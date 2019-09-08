#!/bin/sh
mkdir -p build
rm -f build/*

files="manifest.json options.html options.js sidebar.css sidebar.html sidebar.js"
zip -1 build/clk2@kidneybone.com.xpi $files
