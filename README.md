# dxfcriber

dxfcriber extracts TEXT entities from DXF files and outputs their data to a CSV file.

## Example

```bash
dxfcriber *.dxf -r 1 --ymin 100 --ymax 280 -c Column1,301,330 -c Column2,331,360 -c Column2,361,390 > output.csv
```

## License

[MIT License](LICENSE)
