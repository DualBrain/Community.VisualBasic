﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Imports System
Imports System.Globalization
Imports System.Text

Imports Community.VisualBasic
Imports Community.VisualBasic.CompilerServices.ExceptionUtils
Imports Community.VisualBasic.CompilerServices.Utils

Namespace Global.Community.VisualBasic.CompilerServices

  <System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)>
  Public NotInheritable Class StringType
    ' Prevent creation.
    Private Sub New()
    End Sub

    Private Const GENERAL_FORMAT As String = "G"

    '============================================================================
    ' Coercion to functions.
    '============================================================================
    Public Shared Function FromBoolean(Value As Boolean) As String
      If Value Then
        Return System.Boolean.TrueString
      Else
        Return System.Boolean.FalseString
      End If
    End Function

    Public Shared Function FromByte(Value As Byte) As String
      Return Value.ToString(Nothing, Nothing)
    End Function

    Public Shared Function FromChar(Value As Char) As String
      Return Value.ToString()
    End Function

    Public Shared Function FromShort(Value As Short) As String
      Return Value.ToString(Nothing, Nothing)
    End Function

    Public Shared Function FromInteger(Value As Integer) As String
      Return Value.ToString(Nothing, Nothing)
    End Function

    Public Shared Function FromLong(Value As Long) As String
      Return Value.ToString(Nothing, Nothing)
    End Function

    Public Shared Function FromSingle(Value As Single) As String
      Return FromSingle(Value, Nothing)
    End Function

    Public Shared Function FromDouble(Value As Double) As String
      Return FromDouble(Value, Nothing)
    End Function

    'Change to this code after the NDP drop includes the formatting changes
    Public Shared Function FromSingle(Value As Single, NumberFormat As NumberFormatInfo) As String
      Return Value.ToString(Nothing, NumberFormat)
    End Function

    Public Shared Function FromDouble(Value As Double, NumberFormat As NumberFormatInfo) As String
      Return Value.ToString("G", NumberFormat)
    End Function

    Public Shared Function FromDate(Value As Date) As String
      Dim TimeTicks As Long = Value.TimeOfDay.Ticks

      If (TimeTicks = Value.Ticks) OrElse
          (Value.Year = 1899 AndAlso Value.Month = 12 AndAlso Value.Day = 30) Then 'OA Date with no date is 1899-12-30
        'No date (1/1/1)
        Return Value.ToString("T", Nothing)
      ElseIf TimeTicks = 0 Then
        'No time, or is midnight
        Return Value.ToString("d", Nothing)
      Else
        Return Value.ToString(GENERAL_FORMAT, Nothing)
      End If
    End Function

    Public Shared Function FromDecimal(Value As Decimal) As String
      Return FromDecimal(Value, Nothing)
    End Function

    Public Shared Function FromDecimal(Value As Decimal, NumberFormat As NumberFormatInfo) As String
      Return Value.ToString("G", NumberFormat)
    End Function

    Public Shared Function FromObject(Value As Object) As String

      If Value Is Nothing Then
        Return Nothing

      Else
        Dim StringValue As String = TryCast(Value, String)

        If StringValue IsNot Nothing Then
          Return StringValue
        End If
      End If

      Dim ValueInterface As IConvertible
      Dim ValueTypeCode As TypeCode

      ValueInterface = TryCast(Value, IConvertible)

      If ValueInterface IsNot Nothing Then

        ValueTypeCode = ValueInterface.GetTypeCode()

        Select Case ValueTypeCode
          Case TypeCode.Boolean
            Return FromBoolean(ValueInterface.ToBoolean(Nothing))

          Case TypeCode.Byte
            Return FromByte(ValueInterface.ToByte(Nothing))

          Case TypeCode.Int16
            Return FromShort(ValueInterface.ToInt16(Nothing))

          Case TypeCode.Int32
            Return FromInteger(ValueInterface.ToInt32(Nothing))

          Case TypeCode.Int64
            Return FromLong(ValueInterface.ToInt64(Nothing))

          Case TypeCode.Single
            Return FromSingle(ValueInterface.ToSingle(Nothing))

          Case TypeCode.Double
            Return FromDouble(ValueInterface.ToDouble(Nothing))

          Case TypeCode.Decimal
            Return FromDecimal(ValueInterface.ToDecimal(Nothing))

          Case TypeCode.String
            Return ValueInterface.ToString(Nothing)

          Case TypeCode.Char
            Return FromChar(ValueInterface.ToChar(Nothing))

          Case TypeCode.DateTime
            Return FromDate(ValueInterface.ToDateTime(Nothing))

          Case Else
            ' Fall through to error
        End Select

      Else
        Dim CharArray As Char() = TryCast(Value, Char())

        If CharArray IsNot Nothing AndAlso CharArray.Rank = 1 Then
          Return New String(CharArrayType.FromObject(Value))
        End If
      End If

      Throw New InvalidCastException(SR.Format(SR.InvalidCast_FromTo, VBFriendlyName(Value), "String"))

    End Function

    '============================================================================
    ' Compare/concat/len functions.
    '============================================================================
    Public Shared Function StrCmp(sLeft As String, sRight As String, TextCompare As Boolean) As Integer

      If sLeft Is sRight Then
        Return 0
      End If

      If sLeft Is Nothing Then
        If sRight.Length() = 0 Then
          Return 0
        End If

        Return -1
      End If

      If sRight Is Nothing Then
        If sLeft.Length() = 0 Then
          Return 0
        End If

        Return 1
      End If

      If TextCompare Then
        Return GetCultureInfo().CompareInfo.Compare(sLeft, sRight, OptionCompareTextFlags)
      Else
        Return System.String.CompareOrdinal(sLeft, sRight)
      End If

    End Function

    Public Shared Function StrLike(Source As String, Pattern As String, CompareOption As CompareMethod) As Boolean
      If CompareOption = CompareMethod.Binary Then
        Return StrLikeBinary(Source, Pattern)
      Else
        Return StrLikeText(Source, Pattern)
      End If
    End Function

    Public Shared Function StrLikeBinary(Source As String, Pattern As String) As Boolean
      'Match Source to Pattern using "?*#[!a-g]" pattern matching characters
      Dim SourceIndex As Integer
      Dim PatternIndex As Integer
      Dim SourceEndIndex As Integer
      Dim PatternEndIndex As Integer
      Dim p As Char
      Dim s As Char
      Dim InsideBracket As Boolean
      Dim SeenHyphen As Boolean
      Dim StartRangeChar As Char
      Dim EndRangeChar As Char
      Dim Match As Boolean
      Dim SeenLiteral As Boolean
      Dim SeenNot As Boolean
      Dim Skip As Integer
      Const NullChar As Char = ChrW(0)
      Dim LiteralIsRangeEnd As Boolean = False

      '        Options = CompareOptions.Ordinal

      If Pattern Is Nothing Then
        PatternEndIndex = 0
      Else
        PatternEndIndex = Pattern.Length
      End If

      If Source Is Nothing Then
        SourceEndIndex = 0
      Else
        SourceEndIndex = Source.Length
      End If

      If SourceIndex < SourceEndIndex Then
        s = Source.Chars(SourceIndex)
      End If

      Do While PatternIndex < PatternEndIndex
        p = Pattern.Chars(PatternIndex)

        If p = "*"c AndAlso (Not InsideBracket) Then        'If Then Else has faster performance the Select Case
          'Determine how many source chars to skip
          Skip = AsteriskSkip(Pattern.Substring(PatternIndex + 1), Source.Substring(SourceIndex), SourceEndIndex - SourceIndex, CompareMethod.Binary, m_InvariantCompareInfo)

          If Skip < 0 Then
            Return False
          ElseIf Skip > 0 Then
            SourceIndex += Skip
            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            End If
          End If

        ElseIf p = "?"c AndAlso (Not InsideBracket) Then
          'Match any character
          SourceIndex += 1
          If SourceIndex < SourceEndIndex Then
            s = Source.Chars(SourceIndex)
          End If

        ElseIf p = "#"c AndAlso (Not InsideBracket) Then
          If Not System.Char.IsDigit(s) Then
            Exit Do
          End If
          SourceIndex += 1
          If SourceIndex < SourceEndIndex Then
            s = Source.Chars(SourceIndex)
          End If

        ElseIf p = "-"c AndAlso
                (InsideBracket AndAlso SeenLiteral AndAlso (Not LiteralIsRangeEnd) AndAlso (Not SeenHyphen)) AndAlso
                (((PatternIndex + 1) >= PatternEndIndex) OrElse (Pattern.Chars(PatternIndex + 1) <> "]"c)) Then

          SeenHyphen = True

        ElseIf p = "!"c AndAlso
                (InsideBracket AndAlso (Not SeenNot)) Then

          SeenNot = True
          Match = True

        ElseIf p = "["c AndAlso (Not InsideBracket) Then
          InsideBracket = True
          StartRangeChar = NullChar
          'EndRangeChar = NullChar
          SeenLiteral = False

        ElseIf p = "]"c AndAlso InsideBracket Then
          InsideBracket = False

          If SeenLiteral Then
            If Match Then
              SourceIndex += 1
              If SourceIndex < SourceEndIndex Then
                s = Source.Chars(SourceIndex)
              End If
            Else
              Exit Do
            End If
          ElseIf SeenHyphen Then
            If Not Match Then
              Exit Do
            End If
          ElseIf SeenNot Then
            '[!] should be matched to literal ! same as if outside brackets
            If "!"c <> s Then
              Exit Do
            End If
            SourceIndex += 1
            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            End If
          End If

          Match = False
          SeenLiteral = False
          SeenNot = False
          SeenHyphen = False

        Else
          'Literal character
          SeenLiteral = True
          LiteralIsRangeEnd = False

          If InsideBracket Then
            If SeenHyphen Then
              SeenHyphen = False
              LiteralIsRangeEnd = True
              EndRangeChar = p

              If StartRangeChar > EndRangeChar Then
                Throw VbMakeException(vbErrors.BadPatStr)
              ElseIf (SeenNot AndAlso Match) OrElse (Not SeenNot AndAlso Not Match) Then
                'Calls to ci.Compare are expensive, avoid them for good performance
                Match = (s > StartRangeChar) AndAlso (s <= EndRangeChar)

                If SeenNot Then
                  Match = Not Match
                End If
              End If
            Else
              StartRangeChar = p

              'This compare handles non range chars such as the "abc" and "uvw" 
              'and the first char of a range such as "d" in "[abcd-tuvw]".
              Match = StrLikeCompareBinary(SeenNot, Match, p, s)
            End If
          Else
            If p <> s AndAlso Not SeenNot Then
              Exit Do
            End If

            SeenNot = False
            SourceIndex += 1

            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            ElseIf SourceIndex > SourceEndIndex Then
              Return False
            End If
          End If
        End If

        PatternIndex += 1
      Loop

      If InsideBracket Then
        If SourceEndIndex = 0 Then
          Return False
        Else
          Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Pattern"))
        End If
      Else
        Return (PatternIndex = PatternEndIndex) AndAlso (SourceIndex = SourceEndIndex)
      End If
    End Function

    Public Shared Function StrLikeText(Source As String, Pattern As String) As Boolean
      'Match Source to Pattern using "?*#[!a-g]" pattern matching characters
      Dim SourceIndex As Integer
      Dim PatternIndex As Integer
      Dim SourceEndIndex As Integer
      Dim PatternEndIndex As Integer
      Dim p As Char
      Dim s As Char
      Dim InsideBracket As Boolean
      Dim SeenHyphen As Boolean
      Dim StartRangeChar As Char
      Dim EndRangeChar As Char
      Dim Match As Boolean
      Dim SeenLiteral As Boolean
      Dim SeenNot As Boolean
      Dim Skip As Integer
      Dim Options As CompareOptions
      Dim ci As CompareInfo
      Const NullChar As Char = ChrW(0)
      Dim LiteralIsRangeEnd As Boolean = False

      If Pattern Is Nothing Then
        PatternEndIndex = 0
      Else
        PatternEndIndex = Pattern.Length
      End If

      If Source Is Nothing Then
        SourceEndIndex = 0
      Else
        SourceEndIndex = Source.Length
      End If

      If SourceIndex < SourceEndIndex Then
        s = Source.Chars(SourceIndex)
      End If

      ci = GetCultureInfo().CompareInfo
      Options = CompareOptions.IgnoreCase Or
                CompareOptions.IgnoreWidth Or
                CompareOptions.IgnoreNonSpace Or
                CompareOptions.IgnoreKanaType

      Do While PatternIndex < PatternEndIndex
        p = Pattern.Chars(PatternIndex)

        If p = "*"c AndAlso (Not InsideBracket) Then        'If Then Else has faster performance the Select Case
          'Determine how many source chars to skip
          Skip = AsteriskSkip(Pattern.Substring(PatternIndex + 1), Source.Substring(SourceIndex), SourceEndIndex - SourceIndex, CompareMethod.Text, ci)

          If Skip < 0 Then
            Return False
          ElseIf Skip > 0 Then
            SourceIndex += Skip
            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            End If
          End If

        ElseIf p = "?"c AndAlso (Not InsideBracket) Then
          'Match any character
          SourceIndex += 1
          If SourceIndex < SourceEndIndex Then
            s = Source.Chars(SourceIndex)
          End If

        ElseIf p = "#"c AndAlso (Not InsideBracket) Then
          If Not System.Char.IsDigit(s) Then
            Exit Do
          End If
          SourceIndex += 1
          If SourceIndex < SourceEndIndex Then
            s = Source.Chars(SourceIndex)
          End If

        ElseIf p = "-"c AndAlso
                (InsideBracket AndAlso SeenLiteral AndAlso (Not LiteralIsRangeEnd) AndAlso (Not SeenHyphen)) AndAlso
                (((PatternIndex + 1) >= PatternEndIndex) OrElse (Pattern.Chars(PatternIndex + 1) <> "]"c)) Then

          SeenHyphen = True

        ElseIf p = "!"c AndAlso
                (InsideBracket AndAlso Not SeenNot) Then
          SeenNot = True
          Match = True

        ElseIf p = "["c AndAlso (Not InsideBracket) Then
          InsideBracket = True
          StartRangeChar = NullChar
          'EndRangeChar = NullChar
          SeenLiteral = False

        ElseIf p = "]"c AndAlso InsideBracket Then
          InsideBracket = False

          If SeenLiteral Then
            If Match Then
              SourceIndex += 1
              If SourceIndex < SourceEndIndex Then
                s = Source.Chars(SourceIndex)
              End If
            Else
              Exit Do
            End If
          ElseIf SeenHyphen Then
            If Not Match Then
              Exit Do
            End If
          ElseIf SeenNot Then
            '[!] should be matched to literal ! same as if outside brackets
            If (ci.Compare("!", s) <> 0) Then
              Exit Do
            End If
            SourceIndex += 1
            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            End If
          End If

          Match = False
          SeenLiteral = False
          SeenNot = False
          SeenHyphen = False

        Else
          'Literal character
          SeenLiteral = True
          LiteralIsRangeEnd = False

          If InsideBracket Then
            If SeenHyphen Then
              SeenHyphen = False
              LiteralIsRangeEnd = True
              EndRangeChar = p

              If StartRangeChar > EndRangeChar Then
                Throw VbMakeException(vbErrors.BadPatStr)
              ElseIf (SeenNot AndAlso Match) OrElse (Not SeenNot AndAlso Not Match) Then
                'Calls to ci.Compare are expensive, avoid them for good performance
                If Options = CompareOptions.Ordinal Then
                  Match = (s > StartRangeChar) AndAlso (s <= EndRangeChar)
                Else
                  Match = (ci.Compare(StartRangeChar, s, Options) < 0) AndAlso (ci.Compare(EndRangeChar, s, Options) >= 0)
                End If

                If SeenNot Then
                  Match = Not Match
                End If
              End If
            Else
              StartRangeChar = p

              'This compare handles non range chars such as the "abc" and "uvw" 
              'and the first char of a range such as "d" in "[abcd-tuvw]".
              Match = StrLikeCompare(ci, SeenNot, Match, p, s, Options)
            End If
          Else
            If Options = CompareOptions.Ordinal Then
              If p <> s AndAlso Not SeenNot Then
                Exit Do
              End If
            Else
              ' Slurp up the diacritical marks, if any (both non-spacing marks and modifier symbols)
              ' Note that typically, we'll only have at most one diacritical mark.  Therefore, I'm not
              ' using StringBuilder here, since the minimal overhead of appending a character doesn't
              ' justify invoking a couple of instances of StringBuilder. .
              Dim pstr As String = p
              Dim sstr As String = s
              Do While PatternIndex + 1 < PatternEndIndex AndAlso
                      (UnicodeCategory.ModifierSymbol = Char.GetUnicodeCategory(Pattern.Chars(PatternIndex + 1)) OrElse
                      UnicodeCategory.NonSpacingMark = Char.GetUnicodeCategory(Pattern.Chars(PatternIndex + 1)))
                pstr &= Pattern.Chars(PatternIndex + 1)
                PatternIndex += 1
              Loop
              Do While SourceIndex + 1 < SourceEndIndex AndAlso
                      (UnicodeCategory.ModifierSymbol = Char.GetUnicodeCategory(Source.Chars(SourceIndex + 1)) OrElse
                      UnicodeCategory.NonSpacingMark = Char.GetUnicodeCategory(Source.Chars(SourceIndex + 1)))
                sstr &= Source.Chars(SourceIndex + 1)
                SourceIndex += 1
              Loop

              If (ci.Compare(pstr, sstr, OptionCompareTextFlags) <> 0) AndAlso Not SeenNot Then
                Exit Do
              End If
            End If

            SeenNot = False
            SourceIndex += 1

            If SourceIndex < SourceEndIndex Then
              s = Source.Chars(SourceIndex)
            ElseIf SourceIndex > SourceEndIndex Then
              Return False
            End If
          End If
        End If

        PatternIndex += 1
      Loop

      If InsideBracket Then
        If SourceEndIndex = 0 Then
          Return False
        Else
          Throw New ArgumentException(SR.Format(SR.Argument_InvalidValue1, "Pattern"))
        End If
      Else
        Return (PatternIndex = PatternEndIndex) AndAlso (SourceIndex = SourceEndIndex)
      End If
    End Function

    Private Shared Function StrLikeCompareBinary(SeenNot As Boolean, Match As Boolean, p As Char, s As Char) As Boolean
      If SeenNot AndAlso Match Then
        Return p <> s
      ElseIf Not SeenNot AndAlso Not Match Then
        Return p = s
      Else
        Return Match
      End If
    End Function

    Private Shared Function StrLikeCompare(ci As CompareInfo, SeenNot As Boolean, Match As Boolean, p As Char, s As Char, Options As CompareOptions) As Boolean
      If SeenNot AndAlso Match Then
        If Options = CompareOptions.Ordinal Then
          Return p <> s
        Else
          Return Not (ci.Compare(p, s, Options) = 0)
        End If
      ElseIf Not SeenNot AndAlso Not Match Then
        If Options = CompareOptions.Ordinal Then
          Return p = s
        Else
          Return (ci.Compare(p, s, Options) = 0)
        End If
      Else
        Return Match
      End If
    End Function

    Private Shared Function AsteriskSkip(Pattern As String, Source As String, SourceEndIndex As Integer,
CompareOption As CompareMethod, ci As CompareInfo) As Integer

      'Returns the number of source characters to skip over to handle an asterisk in the pattern. 
      'When there's only a single asterisk in the pattern, it computes how many pattern equivalent chars  
      'follow the *: [a-z], [abcde], ?, # each count as one char.
      'Pattern contains the substring following the *
      'Source contains the substring not yet matched.

      Dim p As Char
      Dim SeenLiteral As Boolean
      Dim SeenSpecial As Boolean   'Remembers if we've seen #, ?, [abd-eg], or ! when they have their special meanings
      Dim InsideBracket As Boolean
      Dim Count As Integer
      Dim PatternEndIndex As Integer
      Dim PatternIndex As Integer
      Dim TruncatedPattern As String
      Dim Options As CompareOptions

      PatternEndIndex = Len(Pattern)

      'Determine how many pattern equivalent chars follow the *, and if there are multiple *s
      '[a-z], [abcde] each count as one char.
      Do While PatternIndex < PatternEndIndex
        p = Pattern.Chars(PatternIndex)

        Select Case p
          Case "*"c
            If Count > 0 Then
              'We found multiple asterisks with an intervening pattern
              If SeenSpecial Then
                'Pattern uses special characters which means we can't compute easily how far to skip. 
                Count = MultipleAsteriskSkip(Pattern, Source, Count, CompareOption)
                Return SourceEndIndex - Count
              Else
                'Pattern uses only literals, so we can directly search for the pattern in the source
                TruncatedPattern = Pattern.Substring(0, PatternIndex)    'Remove the second * and everything trailing  

                If CompareOption = CompareMethod.Binary Then
                  Options = CompareOptions.Ordinal
                Else
                  Options = CompareOptions.IgnoreCase Or CompareOptions.IgnoreWidth Or CompareOptions.IgnoreNonSpace Or CompareOptions.IgnoreKanaType
                End If

                'Count = Source.LastIndexOf(TruncatedPattern)
                Count = ci.LastIndexOf(Source, TruncatedPattern, Options)
                Return Count
              End If

            Else
              'Do nothing, which colalesces multiple asterisks together
            End If

          Case "-"c
            If Pattern.Chars(PatternIndex + 1) = "]"c Then
              SeenLiteral = True
            End If

          Case "!"c
            If Pattern.Chars(PatternIndex + 1) = "]"c Then
              SeenLiteral = True
            Else
              SeenSpecial = True
            End If

          Case "["c
            If InsideBracket Then
              SeenLiteral = True
            Else
              InsideBracket = True
            End If

          Case "]"c
            If SeenLiteral OrElse Not InsideBracket Then
              Count += 1
              SeenSpecial = True
            End If
            SeenLiteral = False
            InsideBracket = False

          Case "?"c, "#"c
            If InsideBracket Then
              SeenLiteral = True
            Else
              Count += 1
              SeenSpecial = True
            End If

          Case Else
            If InsideBracket Then
              SeenLiteral = True
            Else
              Count += 1
            End If
        End Select

        PatternIndex += 1
      Loop

      Return SourceEndIndex - Count
    End Function

    Private Shared Function MultipleAsteriskSkip(Pattern As String, Source As String, Count As Integer, CompareOption As CompareMethod) As Integer
      'Multiple asterisks with intervening chars were found in the pattern, such as "*<chars>*".
      'Use a recursive approach to determine how many source chars to skip.
      'Start near the end of Source and move backwards one char at a time until a match is found or we reach start of Source.

      Dim SourceEndIndex As Integer
      Dim NewSource As String
      Dim Result As Boolean

      SourceEndIndex = Len(Source)

      Do While Count < SourceEndIndex
        NewSource = Source.Substring(SourceEndIndex - Count)

        Try
          Result = StrLike(NewSource, Pattern, CompareOption)
        Catch ex As StackOverflowException
          Throw ex
        Catch ex As OutOfMemoryException
          Throw ex
        Catch
          Result = False
        End Try

        If Result Then
          Exit Do
        End If

        Count += 1
      Loop

      Return Count
    End Function

  End Class

End Namespace