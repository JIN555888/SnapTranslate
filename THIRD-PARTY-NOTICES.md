# 사용한 오픈소스와 외부 서비스

SnapTranslate 는 아래 소프트웨어와 서비스를 사용합니다.

## 오픈소스

### .NET (런타임, Windows Forms)

- 출처: https://github.com/dotnet/runtime , https://github.com/dotnet/winforms
- 라이선스: MIT
- `-self-contained.zip` 배포본에는 .NET 런타임이 함께 들어 있습니다.
- .NET 자체가 포함하는 제3자 구성 요소 고지: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

### C#/WinRT (Windows SDK .NET 투영)

- 출처: https://github.com/microsoft/CsWinRT
- 라이선스: MIT
- Windows 내장 OCR 을 .NET 에서 호출하는 데 사용합니다.

위 두 항목의 라이선스 전문:

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors
Copyright (c) Microsoft Corporation.

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 외부 서비스·운영체제 기능

### 글자 인식 — Windows 내장 OCR

- Windows 에 들어 있는 기능(`Windows.Media.Ocr`)을 호출합니다. 프로그램에 포함해 배포하지 않습니다.
- Windows 는 Microsoft Corporation 의 상표입니다.

### 번역 — Google 번역

- 번역 결과는 Google 번역 서비스가 만든 것입니다.
- SnapTranslate 는 Google 과 제휴·보증 관계가 없는 개인 제작 프로그램입니다.
- 공식 유료 API 가 아닌 웹 번역 주소를 사용하므로, Google 의 정책에 따라 예고 없이 제한되거나 막힐 수 있습니다.
- Google 및 Google 번역은 Google LLC 의 상표입니다.

### 번역 — MyMemory

- Translated 사의 공개 번역 API(`api.mymemory.translated.net`)를 키 없이 사용합니다. 하루 무료 사용량(약 5,000자)이 있습니다.
- 번역 결과는 MyMemory 서비스가 만든 것입니다. SnapTranslate 는 Translated 와 제휴·보증 관계가 없습니다.

### 번역 — Gemini API

- 사용자가 직접 발급받아 입력한 API 키로 Google 의 Gemini API 를 호출합니다. 키는 사용자 PC 의 `config.json` 에만 저장됩니다.
- 사용량·요금·데이터 처리는 사용자의 Google 계정과 Gemini API 약관을 따릅니다.
- Gemini 는 Google LLC 의 상표입니다.

## 글꼴

- 아이콘의 글자 "가"와 화면 기본 글꼴은 Windows 에 설치된 "맑은 고딕"(Microsoft)으로 그렸습니다. 글꼴 파일은 포함해 배포하지 않습니다.
