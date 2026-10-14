### WTFile

WTFile is a simple command-line utility that identifies unknown files, or files with a missing file extension, by matching their binary signature (magic number) against a list of known signatures.

### Instructions

Build the project, then run the executable with the path of the file you would like to check, in quotes:

```
wtfile.exe "C:\location\to\your\file"
```

The file `file_sigs.json` contains the signature list and must sit next to the executable (the build copies it to the output directory automatically). The program prints every matching signature with its description and extension, or a "no matching signature" message with the file's leading bytes in hex.

### Acknowledgements

This project uses the file signatures made available at [Gary Kessler's file signatures table](https://www.garykessler.net/library/file_sigs.html).
