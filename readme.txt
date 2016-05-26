1. Install/Extract WinPython-Zero into temp/python_zero

2. open WinPython Command Prompt.exe
3. pip install gmusicapi

4. Copy temp/python_zero/python-3.X.X to python
   python/python.exe should now exist and 'work'

5. Clone https://github.com/pythonnet/pythonnet.git to temp/pythonnet

6. Copy python to temp/python && Navigate to temp/pythonnet
   NOTE: Use zero python for this.

7. ..\python\python -m pip install wheel
8. ..\python\python -m pip install six

9. Apply https://github.com/pythonnet/pythonnet/pull/194/commits/16ed1d0122425e3669b252c07898b53505309f90 from https://github.com/pythonnet/pythonnet/pull/194

