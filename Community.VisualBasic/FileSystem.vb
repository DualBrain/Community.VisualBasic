﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Runtime.Versioning
Imports System.Text

Imports Community.VisualBasic.CompilerServices
Imports Community.VisualBasic.CompilerServices.ExceptionUtils
Imports Community.VisualBasic.CompilerServices.IOUtils
Imports Community.VisualBasic.CompilerServices.Utils

Namespace Global.Community.VisualBasic

  Public Module FileSystem

    Private Const ERROR_FILE_NOT_FOUND As Integer = 2
    Private Const ERROR_FILE_EXISTS As Integer = 80
    Private Const ERROR_ALREADY_EXISTS As Integer = 183
    Private Const ERROR_INVALID_ACCESS As Integer = 12
    Private Const ERROR_NOT_SAME_DEVICE As Integer = 17

    Friend Enum VbFileType
      vbPrintFile = 0
      vbWriteFile = 1
    End Enum
    'FILESYSTEM function vars

    Friend Const FIRST_LOCAL_CHANNEL As Integer = 1
    Friend Const LAST_LOCAL_CHANNEL As Integer = 255

    Friend Const sTimeFormat As String = "T"
    Friend Const sDateFormat As String = "d"
    Friend Const sDateTimeFormat As String = "F"

    Friend ReadOnly m_WriteDateFormatInfo As DateTimeFormatInfo = InitializeWriteDateFormatInfo() ' Call static initializer due to FxCop InitializeReferenceTypeStaticFieldsInline.
    Private Function InitializeWriteDateFormatInfo() As DateTimeFormatInfo
      Dim dfi As New DateTimeFormatInfo With {.DateSeparator = "-",
                                              .ShortDatePattern = "\#yyyy-MM-dd\#",
                                              .LongTimePattern = "\#HH:mm:ss\#",
                                              .FullDateTimePattern = "\#yyyy-MM-dd HH:mm:ss\#"}
      Return dfi
    End Function

    '============================================================================
    ' Directory/drive functions.
    '============================================================================

    Public Sub ChDir(path As String)
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      path = RTrim(path) 'VB6 accepted things like "\   ", so need to trim the trailing spaces

      If path Is Nothing OrElse path.Length = 0 Then
        Throw VbMakeException(New ArgumentException(SR.Argument_PathNullOrEmpty), VbErrors.BadFileNameOrNumber)
      End If

      ' Do this since System.IO.Directory does not accept "\"
      If path = "\" Then
        path = Directory.GetDirectoryRoot(Directory.GetCurrentDirectory())
      End If

      If RuntimeInformation.IsOSPlatform(OSPlatform.Linux) Then
        path = path.Replace("\", "/")
      End If

      Try
        System.IO.Directory.SetCurrentDirectory(path)
      Catch ex As System.IO.FileNotFoundException
        Throw VbMakeException(New FileNotFoundException(SR.Format(SR.FileSystem_PathNotFound1, path)), VbErrors.PathNotFound)
      End Try

    End Sub

    '<SupportedOSPlatform("windows")>
    Public Sub ChDrive(Drive As Char)

      If OperatingSystem.IsWindows Then

        Drive = System.Char.ToUpperInvariant(Drive)

        If (Drive < chLetterA) OrElse (Drive > chLetterZ) Then
          Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Drive"))
        End If

        If Not UnsafeValidDrive(Drive) Then
          Throw VbMakeException(New IOException(SR.Format(SR.FileSystem_DriveNotFound1, CStr(Drive))), VbErrors.DevUnavailable)
        End If

        IO.Directory.SetCurrentDirectory(Drive & Path.VolumeSeparatorChar)

      Else

        Throw New PlatformNotSupportedException

      End If

    End Sub

    '<SupportedOSPlatform("windows")>
    Public Sub ChDrive(Drive As String)

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If OperatingSystem.IsWindows Then

        If Drive Is Nothing OrElse Drive.Length = 0 Then
          Exit Sub
        End If

        ChDrive(Drive.Chars(0))

      Else

        Throw New PlatformNotSupportedException

      End If

    End Sub

    Public Function CurDir() As String

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      Return Directory.GetCurrentDirectory()

    End Function

    '<SupportedOSPlatform("windows")>
    Public Function CurDir(Drive As Char) As String

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If OperatingSystem.IsWindows Then

        Drive = System.Char.ToUpperInvariant(Drive)
        If (Drive < chLetterA OrElse Drive > chLetterZ) Then
          Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Drive")), VbErrors.DevUnavailable)
        End If

        'GetFullPath("x:.") will return the full directory path
        Dim CurrentPath As String = Path.GetFullPath(Drive & Path.VolumeSeparatorChar & ".")

        If Not UnsafeValidDrive(Drive) Then
          Throw VbMakeException(New IOException(SR.Format(SR.FileSystem_DriveNotFound1, CStr(Drive))), VbErrors.DevUnavailable)
        End If

        Return CurrentPath

      Else

        Throw New PlatformNotSupportedException

      End If

    End Function

    Public Function Dir() As String
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      Return FindNextFile(System.Reflection.Assembly.GetCallingAssembly())
    End Function

    <SupportedOSPlatform("windows")>
    <ResourceExposure(ResourceScope.None)>
    <ResourceConsumption(ResourceScope.Machine, ResourceScope.Machine)>
    Public Function Dir(PathName As String, Optional Attributes As FileAttribute = FileAttribute.Normal) As String
      'VB's FileAttribute is different than System.IO.FileAttributes:
      '		            VB	  URT
      'Normal		         0	  128
      'ReadOnly		     1	    1
      'Hidden		         2	    2
      'System		         4	    4
      'Volume		         8	   --
      'Directory		    16	   16
      'Archive		    32	   32
      'Device	    	    --	   64
      'Temporary		    --	  256
      'SparseFile		    --	  512
      'ReparsePoint	    --	 1024
      'Compressed		    --	 2048
      'Offline		    --	 4096
      'NotContentIndexed	--	 8192
      'Encrypted		    --	16384

      'Note: Do NOT throw if pathName = "".  That's legal for this function - returns the first file found.

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If Attributes = FileAttribute.Volume Then

        If OperatingSystem.IsWindows Then

          Dim Result As Integer
          Dim VolumeName As StringBuilder = New StringBuilder(256)
          Dim RootName As String = Nothing

          If (PathName.Length > 0) Then
            RootName = Path.GetPathRoot(PathName)

            'Add a backslash if one isn't there. This is required by GetVolumeInformation
            If RootName.Chars(RootName.Length - 1) <> Path.DirectorySeparatorChar Then
              RootName &= Path.DirectorySeparatorChar
            End If
          End If

          Result = NativeMethods.GetVolumeInformation(RootName, VolumeName, 256, 0, 0, 0, Nothing, 0)

          If Result <> 0 Then
            Return VolumeName.ToString
          Else
            Return ""
          End If

        Else

          Throw New PlatformNotSupportedException()

        End If

      Else

        'Dir function always returns files with Normal attribute in addition to others specified.
        Dim URTAttributes As System.IO.FileAttributes = CType(Attributes, FileAttributes) Or FileAttributes.Normal

        Return FindFirstFile(System.Reflection.Assembly.GetCallingAssembly(), PathName, URTAttributes)

      End If

    End Function

    Public Sub MkDir(Path As String)
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If Path Is Nothing OrElse Path.Length = 0 Then
        Throw VbMakeException(New ArgumentException(SR.Argument_PathNullOrEmpty), VbErrors.BadFileNameOrNumber)
      End If

      If Directory.Exists(Path) Then
        Throw VbMakeException(VbErrors.PathFileAccess)
      Else
        Directory.CreateDirectory(Path)
      End If
    End Sub

    Public Sub RmDir(Path As String)
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      'If null or empty directory, give error
      If Path Is Nothing OrElse Path.Length = 0 Then
        Throw VbMakeException(New ArgumentException(SR.Argument_PathNullOrEmpty), VbErrors.BadFileNameOrNumber)
      End If

      Try
        Directory.Delete(Path)
      Catch e1 As DirectoryNotFoundException
        Throw VbMakeException(e1, VbErrors.PathNotFound)
      Catch ex As StackOverflowException
        Throw ex
      Catch ex As OutOfMemoryException
        Throw ex
      Catch e2 As Exception
        Throw VbMakeException(e2, VbErrors.PathFileAccess)
      End Try
    End Sub

    '============================================================================
    ' File functions.
    '============================================================================

    Private Function PathContainsWildcards(Path As String) As Boolean
      If Path Is Nothing Then
        Return False
      End If

      If (Path.IndexOf("*"c) <> -1) Then
        Return True
      End If

      If (Path.IndexOf("?"c) <> -1) Then
        Return True
      End If

      Return False
    End Function

    Public Sub FileCopy(Source As String, Destination As String)
      If (Source Is Nothing) OrElse (Source.Length = 0) Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_PathNullOrEmpty1, "Source")), VbErrors.BadFileNameOrNumber)
      End If

      If (Destination Is Nothing) OrElse (Destination.Length = 0) Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_PathNullOrEmpty1, "Destination")), VbErrors.BadFileNameOrNumber)
      End If

      '  Error if wildcard characters in name
      If PathContainsWildcards(Source) Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Source")), VbErrors.BadFileNameOrNumber)
      End If

      If PathContainsWildcards(Destination) Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Destination")), VbErrors.BadFileNameOrNumber)
      End If

      Dim oAssemblyData As AssemblyData = ProjectData.GetProjectData().GetAssemblyData(System.Reflection.Assembly.GetCallingAssembly())

      If CheckFileOpen(oAssemblyData, Destination, OpenModeTypes.Output) Then
        Throw VbMakeException(New IOException(SR.Format(SR.FileSystem_FileAlreadyOpen1, Destination)), VbErrors.FileAlreadyOpen)
      End If

      If CheckFileOpen(oAssemblyData, Source, OpenModeTypes.Input) Then
        Throw VbMakeException(New IOException(SR.Format(SR.FileSystem_FileAlreadyOpen1, Source)), VbErrors.FileAlreadyOpen)
      End If

      Try
        File.Copy(Source, Destination, True)

        'VB6 did not copy file attributes, so we must be backwards compatible
        File.SetAttributes(Destination, FileAttributes.Archive)

        'Need to emulate vb6 error codes as much as possible
      Catch ex As FileNotFoundException
        Throw VbMakeException(ex, VbErrors.FileNotFound)
      Catch ex As IOException
        Throw VbMakeException(ex, VbErrors.FileAlreadyOpen)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Function FileDateTime(PathName As String) As DateTime
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If PathContainsWildcards(PathName) Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "PathName")), VbErrors.BadFileNameOrNumber)
      End If

      If File.Exists(PathName) Then
        Return (New FileInfo(PathName)).LastWriteTime
      End If

      Throw New FileNotFoundException(SR.Format(SR.FileSystem_FileNotFound1, PathName))
    End Function

    Public Function FileLen(PathName As String) As Long
      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      If File.Exists(PathName) Then
        Return (New FileInfo(PathName)).Length
      End If

      Throw New FileNotFoundException(SR.Format(SR.FileSystem_FileNotFound1, PathName))
    End Function

    Public Function GetAttr(PathName As String) As FileAttribute
      'VB's FileAttribute is different than System.IO.FileAttributes:
      '                   VB    URT
      'Normal		         0	  128
      'ReadOnly		     1	    1
      'Hidden		         2	    2
      'System		         4	    4
      'Volume		         8	   --
      'Directory		    16	   16
      'Archive		    32	   32
      'Device	    	    --	   64
      'Temporary		    --	  256
      'SparseFile		    --	  512
      'ReparsePoint	    --	 1024
      'Compressed		    --	 2048
      'Offline		    --	 4096
      'NotContentIndexed	--	 8192
      'Encrypted		    --	16384

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      Dim WildCards() As Char = {"*"c, "?"c}

      If PathName.IndexOfAny(WildCards) >= 0 Then
        Throw VbMakeException(VbErrors.BadFileNameOrNumber)
      End If

      Dim f As New FileInfo(PathName)

      If f.Exists Then
        'Mask off any attributes that VB doesn't define.
        Return CType(f.Attributes And &H3F, FileAttribute)
      Else
        Dim d As New DirectoryInfo(PathName)
        If d.Exists Then
          'Mask off any attributes that VB doesn't define.
          Return CType(d.Attributes And &H3F, FileAttribute)
        End If
      End If

      If Path.GetFileName(PathName).Length = 0 Then
        Throw VbMakeException(VbErrors.BadFileNameOrNumber)
      Else
        Throw New FileNotFoundException(SR.Format(SR.FileSystem_FileNotFound1, PathName))
      End If

    End Function

    Public Sub Kill(PathName As String)

      Debug.Assert(System.Reflection.Assembly.GetCallingAssembly() IsNot Utils.VBRuntimeAssembly,
          "Methods in Microsoft.VisualBasic should not call FileSystem public method.")

      Dim dir As DirectoryInfo
      Dim DirName As String
      Dim FileName As String
      Dim files() As FileInfo
      Dim file As FileInfo
      Dim DeleteCount As Integer
      Dim i As Integer

      DirName = Path.GetDirectoryName(PathName)

      If (DirName Is Nothing) OrElse (DirName.Length = 0) Then
        DirName = Environment.CurrentDirectory
        FileName = PathName
      Else
        FileName = Path.GetFileName(PathName)
      End If

      dir = New DirectoryInfo(DirName)
      files = dir.GetFiles(FileName)
      'DirName &= Path.PathSeparator

      If (files IsNot Nothing) Then
        For i = 0 To files.GetUpperBound(0)
          file = files(i)

          'Don't delete hidden or system files
          If (file.Attributes And (FileAttribute.Hidden Or FileAttribute.System)) = 0 Then
            FileName = file.FullName

            '  error if file is presently open
            Dim oAssemblyData As AssemblyData = ProjectData.GetProjectData().GetAssemblyData(System.Reflection.Assembly.GetCallingAssembly())
            If CheckFileOpen(oAssemblyData, FileName, OpenModeTypes.Any) Then
              Throw VbMakeException(New IOException(SR.Format(SR.FileSystem_FileAlreadyOpen1, FileName)), VbErrors.FileAlreadyOpen)
            End If

            Try

              IO.File.Delete(FileName)
              DeleteCount += 1
            Catch ex As IOException
              'Need to emulate vb6 error codes as much as possible
              Throw VbMakeException(ex, VbErrors.FileAlreadyOpen)

            Catch ex As Exception
              Throw ex
            End Try

          End If
        Next i
      End If

      If DeleteCount = 0 Then
        Throw New IO.FileNotFoundException(SR.Format(SR.KILL_NoFilesFound1, PathName))
      End If

    End Sub

    Public Sub SetAttr(PathName As String, Attributes As FileAttribute)
      'VB's FileAttribute is different than System.IO.FileAttributes:
      '                   VB    URT
      'Normal              0    128
      'ReadOnly            1      1
      'Hidden              2      2
      'System              4      4
      'Volume              8     --
      'Directory          16     16
      'Archive            32     32
      'Device             --     64
      'Temporary          --    256
      'SparseFile         --    512
      'ReparsePoint       --   1024
      'Compressed         --   2048
      'Offline            --   4096
      'NotContentIndexed  --   8192
      'Encrypted          --  16384

      'Check pathname for errors and if file is open for any mode except sequential input
      If (PathName Is Nothing) OrElse (PathName.Length = 0) Then
        Throw VbMakeException(New ArgumentException(SR.Argument_PathNullOrEmpty), VbErrors.BadFileNameOrNumber)
      End If

      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Dim oAssemblyData As AssemblyData = ProjectData.GetProjectData().GetAssemblyData(assem)

      VB6CheckPathname(oAssemblyData, PathName, OpenMode.Input)

      'Only allow _A_RDONLY(1), _A_HIDDEN(2), _A_SYSTEM(4), _A_ARCH(20)
      If ((Attributes Or &H27S) <> &H27S) Then
        Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Attributes"))
      End If

      'Dir function always returns files with Normal attribute in addition to others specified.
      Dim URTAttributes As System.IO.FileAttributes = CType(Attributes, FileAttributes)
      System.IO.File.SetAttributes(PathName, URTAttributes)
    End Sub

    'IMPORTANT: This call provides sensitive information whether a device exists and should be used with extreme care
    Private Function UnsafeValidDrive(cDrive As Char) As Boolean 'Return of True means not a valid drive
      If OperatingSystem.IsWindows Then
        Dim iDrive As Integer = AscW(cDrive) - AscW(chLetterA)
        Return (CLng(UnsafeNativeMethods.GetLogicalDrives()) And CLng(&H2 ^ iDrive)) <> 0
      Else
        Throw New PlatformNotSupportedException()
      End If
    End Function

    '*****************************************
    ' FileSystem APIs
    '*****************************************
    Private Sub ValidateAccess(Access As OpenAccess)
      If Access <> OpenAccess.Default AndAlso
          Access <> OpenAccess.Read AndAlso
          Access <> OpenAccess.ReadWrite AndAlso
          Access <> OpenAccess.Write Then
        Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Access"))
      End If
    End Sub

    Private Sub ValidateShare(Share As OpenShare)
      If Share <> OpenShare.Default AndAlso
          Share <> OpenShare.Shared AndAlso
          Share <> OpenShare.LockRead AndAlso
          Share <> OpenShare.LockReadWrite AndAlso
          Share <> OpenShare.LockWrite Then
        Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Share"))
      End If
    End Sub

    Private Sub ValidateMode(Mode As OpenMode)
      If Mode <> OpenMode.Input AndAlso
          Mode <> OpenMode.Output AndAlso
          Mode <> OpenMode.Random AndAlso
          Mode <> OpenMode.Append AndAlso
          Mode <> OpenMode.Binary Then
        Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Mode"))
      End If
    End Sub

    '============================================================================
    ' Initialization functions.
    '============================================================================
    '======================================
    ' Public APIs
    '======================================
    Public Sub FileOpen(FileNumber As Integer,
                        FileName As String,
                        Mode As OpenMode,
                        Optional Access As OpenAccess = OpenAccess.Default,
                        Optional Share As OpenShare = OpenShare.Default,
                        Optional RecordLength As Integer = -1)

      Try
        ValidateMode(Mode)
        ValidateAccess(Access)
        ValidateShare(Share)

        If (FileNumber < FIRST_LOCAL_CHANNEL OrElse FileNumber > LAST_LOCAL_CHANNEL) Then
          Throw VbMakeException(VbErrors.BadFileNameOrNumber)
        End If
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        VbIOOpenFile(assem, FileNumber, FileName, Mode, Access, Share, RecordLength)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileClose(ParamArray FileNumbers() As Integer)
      'If the paramarray is empty, then all files get closed

      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        Dim oAssemblyData As AssemblyData

        oAssemblyData = ProjectData.GetProjectData().GetAssemblyData(assem)

        If (FileNumbers Is Nothing) OrElse (FileNumbers.Length = 0) Then
          CloseAllFiles(oAssemblyData)
        Else
          Dim Index As Integer

          For Index = 0 To FileNumbers.GetUpperBound(0)
            InternalCloseFile(oAssemblyData, FileNumbers(Index))
          Next
        End If
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Private Sub ValidateGetPutRecordNumber(RecordNumber As Long)
      If RecordNumber < 1 AndAlso RecordNumber <> -1 Then
        Throw VbMakeException(New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "RecordNumber")), VbErrors.BadRecordNum)
      End If
    End Sub

    Public Sub FileGetObject(FileNumber As Integer, ByRef Value As Object, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).GetObject(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As ValueType, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As System.Array, Optional RecordNumber As Long = -1,
        Optional ArrayIsDynamic As Boolean = False, Optional StringIsFixedLength As Boolean = False)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber, ArrayIsDynamic, StringIsFixedLength)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Boolean, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Byte, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Short, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Integer, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Long, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Char, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Single, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Double, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Decimal, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As String, Optional RecordNumber As Long = -1, Optional StringIsFixedLength As Boolean = False)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber, StringIsFixedLength)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FileGet(FileNumber As Integer, ByRef Value As Date, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Get(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePutObject(FileNumber As Integer, Value As Object, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).PutObject(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    <ObsoleteAttribute("This member has been deprecated. Please use FilePutObject to write Object types, or coerce FileNumber and RecordNumber to Integer for writing non-Object types. https://go.microsoft.com/fwlink/?linkid=14202")>
    Public Sub FilePut(FileNumber As Object, Value As Object, Optional RecordNumber As Object = -1)
      Throw New ArgumentException(SR.UseFilePutObject)
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As ValueType, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As System.Array, Optional RecordNumber As Long = -1,
        Optional ArrayIsDynamic As Boolean = False, Optional StringIsFixedLength As Boolean = False)

      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber, ArrayIsDynamic, StringIsFixedLength)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Boolean, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Byte, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Short, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Integer, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Long, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Char, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Single, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Double, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Decimal, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As String, Optional RecordNumber As Long = -1, Optional StringIsFixedLength As Boolean = False)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber, StringIsFixedLength)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub FilePut(FileNumber As Integer, Value As Date, Optional RecordNumber As Long = -1)
      Try
        ValidateGetPutRecordNumber(RecordNumber)
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber, OpenModeTypes.Binary Or OpenModeTypes.Random).Put(Value, RecordNumber)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Print(FileNumber As Integer, ParamArray Output() As Object)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Print(CType(Output, Object()))
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub PrintLine(FileNumber As Integer, ParamArray Output() As Object)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).PrintLine(CType(Output, Object()))
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Object)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Boolean)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Byte)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Short)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Integer)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Long)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Char)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Single)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Double)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Decimal)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As String)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub Input(FileNumber As Integer, ByRef Value As Date)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).Input(Value)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub [Write](FileNumber As Integer, ParamArray Output() As Object)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).WriteHelper(Output)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Sub WriteLine(FileNumber As Integer, ParamArray Output() As Object)
      Try
        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
        GetStream(assem, FileNumber).WriteLineHelper(Output)
      Catch ex As Exception
        Throw ex
      End Try
    End Sub

    Public Function InputString(FileNumber As Integer, CharCount As Integer) As String
      Try
        Dim oFile As VB6File

        If (CharCount < 0 OrElse CharCount > (&H7FFFFFFFI / 2)) Then
          Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "CharCount"))
        End If

        Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()

        oFile = GetChannelObj(assem, FileNumber)
        oFile.Lock()

        Try
          InputString = oFile.InputString(CharCount)
        Finally
          oFile.Unlock()
        End Try
      Catch ex As Exception
        Throw ex
      End Try
    End Function

    Public Function [LineInput](FileNumber As Integer) As String
      Dim oFile As VB6File

      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      oFile = GetStream(assem, FileNumber)
      CheckInputCapable(oFile)

      If oFile.EOF() Then
        Throw VbMakeException(VbErrors.EndOfFile)
      End If

      Return oFile.LineInput()
    End Function

    Public Sub Lock(FileNumber As Integer)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Lock()
    End Sub

    Public Sub Lock(FileNumber As Integer, Record As Long)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Lock(Record)
    End Sub

    Public Sub Lock(FileNumber As Integer, FromRecord As Long, ToRecord As Long)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Lock(FromRecord, ToRecord)
    End Sub

    Public Sub Unlock(FileNumber As Integer)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Unlock()
    End Sub

    Public Sub Unlock(FileNumber As Integer, Record As Long)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Unlock(Record)
    End Sub

    Public Sub Unlock(FileNumber As Integer, FromRecord As Long, ToRecord As Long)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Unlock(FromRecord, ToRecord)
    End Sub

    Public Sub FileWidth(FileNumber As Integer, RecordWidth As Integer)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).SetWidth(RecordWidth)
    End Sub

    Public Function [FreeFile]() As Integer
      Dim indChannel As Integer
      Dim oFile As VB6File

      ' get the project object
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Dim oAssemblyData As AssemblyData

      oAssemblyData = ProjectData.GetProjectData().GetAssemblyData(assem)

      For indChannel = 1 To 255
        oFile = oAssemblyData.GetChannelObj(indChannel)
        If oFile Is Nothing Then
          Return indChannel
        End If
      Next

      Throw VbMakeException(VbErrors.TooManyFiles)
    End Function

    'Function Seek
    '
    'RANDOM MODE - Sets the number of next record to read/write
    'other modes - Sets the byte position at which the next operation 
    '              will take place
    '
    Public Sub Seek(FileNumber As Integer, Position As Long)
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      GetStream(assem, FileNumber).Seek(Position)
    End Sub

    'Function Seek
    '
    'RANDOM MODE - Returns number of next record
    'other modes - Returns the byte position at which the next operation 
    '              will take place
    '
    Public Function Seek(FileNumber As Integer) As Long
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Return GetStream(assem, FileNumber).Seek()
    End Function

    Public Function EOF(FileNumber As Integer) As Boolean
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Return GetStream(assem, FileNumber).EOF()
    End Function

    Public Function Loc(FileNumber As Integer) As Long
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Return GetStream(assem, FileNumber).LOC()
    End Function

    Public Function LOF(FileNumber As Integer) As Long
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Return GetStream(assem, FileNumber).LOF()
    End Function

    Public Function TAB() As TabInfo
      Dim Result As TabInfo
      Result.Column = -1
      Return Result
    End Function

    Public Function TAB(Column As Short) As TabInfo
      Dim Result As TabInfo
      If Column < 1 Then
        Column = 1
      End If

      Result.Column = Column
      Return Result
    End Function

    Public Function SPC(Count As Short) As SpcInfo
      Dim Result As SpcInfo
      If Count < 1 Then
        Count = 0
      End If

      Result.Count = Count
      Return Result
    End Function

    Public Function FileAttr(FileNumber As Integer) As OpenMode
      Dim assem As System.Reflection.Assembly = System.Reflection.Assembly.GetCallingAssembly()
      Return GetStream(assem, FileNumber).GetMode()
    End Function

    Public Sub Reset()
      CloseAllFiles(System.Reflection.Assembly.GetCallingAssembly())
    End Sub

    <SupportedOSPlatform("windows")>
    <ResourceExposure(ResourceScope.Machine)>
    <ResourceConsumption(ResourceScope.Machine)>
    Public Sub Rename(OldPath As String, NewPath As String)

      Dim oAssemblyData As AssemblyData = ProjectData.GetProjectData().GetAssemblyData(System.Reflection.Assembly.GetCallingAssembly())
      OldPath = VB6CheckPathname(oAssemblyData, OldPath, CType(OpenModeTypes.Any, OpenMode))
      NewPath = VB6CheckPathname(oAssemblyData, NewPath, CType(OpenModeTypes.Any, OpenMode))

      If OperatingSystem.IsWindows Then

        Dim Result As Integer
        Dim ErrCode As Integer

        Result = UnsafeNativeMethods.MoveFile(OldPath, NewPath)
        If Result = 0 Then
          ErrCode = System.Runtime.InteropServices.Marshal.GetLastWin32Error()

          Select Case ErrCode
            Case ERROR_FILE_NOT_FOUND
              Throw VbMakeException(vbErrors.FileNotFound)

            Case ERROR_FILE_EXISTS,
                         ERROR_ALREADY_EXISTS
              Throw VbMakeException(vbErrors.FileAlreadyExists)

            Case ERROR_INVALID_ACCESS
              Throw VbMakeException(vbErrors.PathFileAccess)

            Case ERROR_NOT_SAME_DEVICE
              Throw VbMakeException(vbErrors.DifferentDrive)

            Case Else
              Throw VbMakeException(vbErrors.IllegalFuncCall)
          End Select
        End If

      Else

        Throw New PlatformNotSupportedException()

      End If

    End Sub

    '======================================
    'Private APIs
    '======================================
    Private Function GetStream(assem As System.Reflection.Assembly, FileNumber As Integer) As VB6File
      Return GetStream(assem, FileNumber, CType(OpenModeTypes.Input Or
                                      OpenModeTypes.Output Or
                                      OpenModeTypes.Random Or
                                      OpenModeTypes.Append Or
                                      OpenModeTypes.Binary, OpenModeTypes))
    End Function

    Private Function GetStream(assem As System.Reflection.Assembly, FileNumber As Integer, mode As OpenModeTypes) As VB6File
      Dim Result As VB6File
      If (FileNumber < FIRST_LOCAL_CHANNEL) OrElse (FileNumber > LAST_LOCAL_CHANNEL) Then
        Throw VbMakeException(VbErrors.BadFileNameOrNumber)
      End If

      Result = GetChannelObj(assem, FileNumber)

      If (OpenModeTypesFromOpenMode(Result.GetMode()) Or mode) = 0 Then
        'Result = Nothing
        Throw VbMakeException(VbErrors.BadFileMode)
      End If

      Return Result
    End Function

    Private Function OpenModeTypesFromOpenMode(om As OpenMode) As OpenModeTypes

      If (om = OpenMode.Input) Then
        Return OpenModeTypes.Input
      ElseIf (om = OpenMode.Output) Then
        Return OpenModeTypes.Output
      ElseIf (om = OpenMode.Append) Then
        Return OpenModeTypes.Append
      ElseIf (om = OpenMode.Binary) Then
        Return OpenModeTypes.Binary
      ElseIf (om = OpenMode.Random) Then
        Return OpenModeTypes.Random
      ElseIf CInt(om) = CInt(OpenModeTypes.Any) Then
        Return OpenModeTypes.Any
      End If

      ' This exception should never be hit.
      ' We will throw Arguments are not valid.
      Throw New ArgumentException(SR.Argument_InvalidValue, NameOf(om))

    End Function

    Friend Sub CloseAllFiles(assem As System.Reflection.Assembly)
      CloseAllFiles(ProjectData.GetProjectData().GetAssemblyData(assem))
    End Sub

    Friend Sub CloseAllFiles(oAssemblyData As AssemblyData)
      Dim FileNumber As Integer

      For FileNumber = 1 To 255
        InternalCloseFile(oAssemblyData, FileNumber)
      Next
    End Sub

    Private Sub InternalCloseFile(oAssemblyData As AssemblyData, FileNumber As Integer)
      If FileNumber = 0 Then
        CloseAllFiles(oAssemblyData)
        Exit Sub
      End If

      Dim oFile As VB6File

      oFile = GetChannelOrNull(oAssemblyData, FileNumber)

      If oFile Is Nothing Then
      Else
        oAssemblyData.SetChannelObj(FileNumber, Nothing)

        If oFile IsNot Nothing Then ' FileNumber not opened
          oFile.CloseFile()
        End If
      End If
    End Sub

    Friend Function VB6CheckPathname(oAssemblyData As AssemblyData, sPath As String, mode As OpenMode) As String
      Dim Result As String
      '  Error if wildcard characters in pathname
      If (sPath.IndexOf("?"c) <> -1 OrElse sPath.IndexOf("*"c) <> -1) Then
        Throw New ArgumentException(SR.Format(SR.Argument_InvalidPathChars1, sPath))
      End If

      '  process the name to check for errors
      Result = (New FileInfo(sPath)).FullName

      '  Error if file is already open and conflicting mode
      If CheckFileOpen(oAssemblyData, Result, OpenModeTypesFromOpenMode(mode)) Then
        Throw VbMakeException(VbErrors.FileAlreadyOpen)
      End If

      Return Result
    End Function

    Friend Function CheckFileOpen(oAssemblyData As AssemblyData, sPath As String, NewFileMode As OpenModeTypes) As Boolean
      Dim lChannel As Integer
      Dim lIndexMax As Integer
      Dim mode As OpenMode
      Dim oFile As VB6File

      lIndexMax = 255

      For lChannel = 1 To lIndexMax
        oFile = GetChannelOrNull(oAssemblyData, lChannel)
        If oFile Is Nothing Then
          'continue looking
        Else
          mode = oFile.GetMode()

          ' compare the filename with the input string case insensitive 
          ' exit loop if match occurs and both files are not sequential input 
          ' and not random/binary.
          If System.String.Compare(sPath, oFile.GetAbsolutePath(), StringComparison.OrdinalIgnoreCase) = 0 Then
            ' If path is the same, then verify
            ' that neither file is open for sequential input
            ' and that both are open for the same mode (either Binary or Random)
            If CInt(NewFileMode) = -1 Then
              'Special case for any open mode
              Return True
            Else
              If (NewFileMode Or mode) <> OpenMode.Input Then
                If (NewFileMode Or mode Or OpenModeTypes.Binary Or OpenModeTypes.Random) <> (OpenModeTypes.Binary Or OpenModeTypes.Random) Then
                  Return True
                End If
              End If
            End If
          End If
        End If
      Next

      Return False
    End Function

    Private Sub VbIOOpenFile(assem As System.Reflection.Assembly,
                             FileNumber As Integer,
                             FileName As String,
                             Mode As OpenMode,
                             Access As OpenAccess,
                             Share As OpenShare,
                             RecordLength As Integer)
      Dim oFile As VB6File
      Dim oAssemblyData As AssemblyData

      oAssemblyData = ProjectData.GetProjectData().GetAssemblyData(assem)

      If GetChannelOrNull(oAssemblyData, FileNumber) IsNot Nothing Then
        Throw VbMakeException(VbErrors.FileAlreadyOpen)
      End If

      If (FileName Is Nothing) OrElse (FileName.Length = 0) Then
        Throw VbMakeException(VbErrors.PathFileAccess)
      End If

      FileName = (New FileInfo(FileName)).FullName

      If CheckFileOpen(oAssemblyData, FileName, OpenModeTypesFromOpenMode(Mode)) Then
        Throw VbMakeException(VbErrors.FileAlreadyOpen)
      End If

      If (RecordLength <> -1 AndAlso RecordLength <= 0) Then
        Throw VbMakeException(VbErrors.IllegalFuncCall)
      End If

      If Mode = OpenMode.Binary Then
        RecordLength = 1
      ElseIf RecordLength = -1 Then
        If Mode = OpenMode.Random Then
          RecordLength = 128
        Else
          RecordLength = 512
        End If
      End If

      '------------------------------------------------------------------
      '   possible combinations of mode and access, and order of access
      '   (other combinations are not passed to rtFileOpen.)
      '       
      '   mode = MODE_SEQ_IN
      '       access = ACCESS_NONE       read
      '       access = ACCESS_READ       read
      '
      '   mode = MODE_SEQ_OUT
      '       access = ACCESS_NONE       write
      '       access = ACCESS_WRITE      write
      '
      '   mode = MODE_RANDOM or MODE_BINARY
      '       access = ACCESS_NONE       read/write, write, read
      '       access = ACCESS_READ       read
      '       access = ACCESS_WRITE      write
      '       access = ACCESS_READ_WRITE read/write
      '
      '   mode = MODE_SEQ_APP
      '       access = ACCESS_NONE       read/write, write
      '       access = ACCESS_WRITE      write
      '------------------------------------------------------------------

      If Share = OpenShare.Default Then
        Share = OpenShare.LockReadWrite
      End If

      Select Case Mode

        Case OpenMode.Input
          If (Access <> OpenAccess.Read) AndAlso (Access <> OpenAccess.Default) Then
            Throw New ArgumentException(SR.FileSystem_IllegalInputAccess)
          End If
          oFile = New VB6InputFile(FileName, Share)
        Case OpenMode.Output
          If (Access <> OpenAccess.Write) AndAlso (Access <> OpenAccess.Default) Then
            Throw New ArgumentException(SR.FileSystem_IllegalOutputAccess)
          End If
          oFile = New VB6OutputFile(FileName, Share, False)
        Case OpenMode.Random
          If (Access = OpenAccess.Default) Then
            Access = OpenAccess.ReadWrite
          End If
          oFile = New VB6RandomFile(FileName, Access, Share, RecordLength)
        Case OpenMode.Append
          If (Access <> OpenAccess.Write) AndAlso (Access <> OpenAccess.ReadWrite) AndAlso (Access <> OpenAccess.Default) Then
            Throw New ArgumentException(SR.FileSystem_IllegalAppendAccess)
          End If
          oFile = New VB6OutputFile(FileName, Share, True)
        Case OpenMode.Binary
          If (Access = OpenAccess.Default) Then
            Access = OpenAccess.ReadWrite
          End If
          oFile = New VB6BinaryFile(FileName, Access, Share)
        Case Else
          Throw VbMakeException(VbErrors.InternalError)
      End Select

      AddFileToList(oAssemblyData, FileNumber, oFile)
    End Sub

    Private Sub AddFileToList(oAssemblyData As AssemblyData, FileNumber As Integer, oFile As VB6File)
      If oFile Is Nothing Then
        Throw VbMakeException(VbErrors.InternalError)
      Else
        oFile.OpenFile()

        oAssemblyData.SetChannelObj(FileNumber, oFile)
      End If
    End Sub

    '======================================
    ' Static methods
    '======================================
    ' GetChannelOrNull() which will throw an exception on bad FileNumber number.
    ' If the table entry is null (e.g. FileNumber is not open) throw an exception
    Friend Function GetChannelObj(assem As System.Reflection.Assembly, FileNumber As Integer) As VB6File
      Dim oFile As VB6File

      oFile = GetChannelOrNull(ProjectData.GetProjectData().GetAssemblyData(assem), FileNumber)

      If oFile Is Nothing Then
        Throw VbMakeException(VbErrors.BadFileNameOrNumber)
      End If

      Return oFile
    End Function

    '======================================
    ' Protected and Private methods
    '======================================
    ' Error an exception only on bad file number.
    ' If the table entry is null, return it.
    Private Function GetChannelOrNull(oAssemblyData As AssemblyData, FileNumber As Integer) As VB6File
      Return oAssemblyData.GetChannelObj(FileNumber)
    End Function

    Private Sub CheckInputCapable(oFile As VB6File)
      If Not oFile.CanInput() Then
        Throw VbMakeException(VbErrors.BadFileMode)
      End If
    End Sub

  End Module

End Namespace