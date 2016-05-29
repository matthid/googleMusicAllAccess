F# wrapper on top of https://github.com/simon-weber/gmusicapi via https://github.com/pythonnet/pythonnet

Alternative: https://github.com/Clancey/gMusic

Currently this works only on 64 bit Windows. It should be straight forward to add other platforms.
Feel free to send Pull-Requests.

Building:

1. Open Git Bash and navigate to project
2. ./build.sh
   This will setup and download everything you need (python + python packages, nuget packages, ...)

Using:

1. Reference GMusicApi (https://www.nuget.org/packages/GMusicApi) nuget package
2. Copy InstallPython.fsx to your project (use paket file references to keep it up2date)
3. Set your platform target to x64
4. Run InstallPython.installPython ("temp/python")
5. Run "temp/python/python.exe -m pip install gmusicapi"
6. Run "temp/python/python.exe -m pip install pythonnet"

// Usually this would be enough, now you can add a reference to 
// temp\python\Lib\site-packages\Python.Runtime.dll and can continue from there.

However currently there are some open issues: https://github.com/pythonnet/pythonnet/pull/219

Copy the following lines https://github.com/matthid/googleMusicAllAccess/blob/master/build.fsx#L77 to get a working pythonnet in your temp python.

Now you can use the library.

You can even test your code in the interactive, see https://github.com/matthid/googleMusicAllAccess/blob/master/src/GMusicApi/Script.fsx