﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.

Option Compare Text
Option Explicit On
Option Infer Off
Option Strict On

Imports System.Collections.Generic
Imports Xunit

Namespace Global.Community.VisualBasic.CompilerServices.Tests

  Public Class LikeOperatorTests
    <Theory>
    <MemberData(NameOf(LikeObject_TestData))>
    <MemberData(NameOf(LikeString_TestData))>
    Public Sub LikeObject(source As Object, pattern As Object, expectedBinaryCompare As Object, expectedTextCompare As Object)
      Assert.Equal(expectedBinaryCompare, Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeObject(source, pattern, Microsoft.VisualBasic.CompareMethod.Binary))
      Assert.Equal(expectedTextCompare, Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeObject(source, pattern, Microsoft.VisualBasic.CompareMethod.Text))
    End Sub
    <Theory>
    <MemberData(NameOf(LikeString_TestData))>
    Public Sub LikeString(source As String, pattern As String, expectedBinaryCompare As Boolean, expectedTextCompare As Boolean)
      Assert.Equal(expectedBinaryCompare, Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeString(source, pattern, Microsoft.VisualBasic.CompareMethod.Binary))
      Assert.Equal(expectedTextCompare, Microsoft.VisualBasic.CompilerServices.LikeOperator.LikeString(source, pattern, Microsoft.VisualBasic.CompareMethod.Text))
    End Sub
    Public Shared Iterator Function LikeObject_TestData() As IEnumerable(Of Object())
      Yield New Object() {Nothing, New Char() {"*"c}, True, True}
      Yield New Object() {Array.Empty(Of Char)(), Nothing, True, True}
      Yield New Object() {"a3", New Char() {"A"c, "#"c}, False, True}
      Yield New Object() {New Char() {"A"c, "3"c}, "a#", False, True}
    End Function

    Public Shared Iterator Function LikeString_TestData() As IEnumerable(Of Object())
      Yield New Object() {Nothing, Nothing, True, True}
      Yield New Object() {Nothing, "*", True, True}
      Yield New Object() {"", Nothing, True, True}
      Yield New Object() {"", "*", True, True}
      Yield New Object() {"", "?", False, False}
      Yield New Object() {"a", "?", True, True}
      Yield New Object() {"a3", "[A-Z]#", False, True}
      Yield New Object() {"A3", "[a-a]#", False, True}
    End Function

  End Class

End Namespace