Add-Type -AssemblyName System.Drawing
$imgPath = $args[0]
$icoPath = $args[1]

$img = [System.Drawing.Image]::FromFile($imgPath)
$bitmap = new-object System.Drawing.Bitmap($img, 256, 256)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$fs = new-object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()
$bitmap.Dispose()
$img.Dispose()
