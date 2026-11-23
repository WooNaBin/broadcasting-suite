# Excel Schedule Parser Implementation Guide (C# & EPPlus)

이 문서는 엑셀로 작성된 스케줄 표의 복잡한 데이터를 추출하여, 어디서나 공용으로 사용할 수 있는 **직렬화된 JSON 데이터**로 변환하는 C# 구현 가이드입니다. 

일반 셀 데이터 추출, 날짜 역추적(추적) 로직, 그리고 격자 구조를 벗어난 **도형(Shape) 내부 텍스트 추출** 기능이 모두 포함되어 있습니다.

---

## 1. 전제 조건 및 패키지 설치

엑셀(.xlsx) 파일의 구조를 정밀하게 제어하기 위해 **EPPlus** 라이브러리를 사용합니다. Microsoft Office 가 설치되어 있지 않은 환경에서도 빠르고 안정적으로 동작합니다.

```bash
# NuGet 패키지 관리자 콘솔에서 설치
Install-Package EPPlus
```

---

## 2. 공용 스케줄 데이터 모델 (Data Model)

다양한 플랫폼(Web, Mobile, DB 등)에서 범용적으로 사용할 수 있도록 표준 포맷으로 직렬화하기 위한 데이터 구조 클래스입니다.

```csharp
using System;

public enum ScheduleSource
{
    Cell,  // 일반 엑셀 셀에서 추출
    Shape  // 격자 외부의 도형(Shape) 내부에서 추출
}

public class ScheduleItem
{
    public string SourceType { get; set; }    // "Cell" 또는 "Shape"
    public string Address { get; set; }       // 엑셀 주소 (예: "B5" 또는 도형 이름 "Rectangle 1")
    public int Row { get; set; }              // 행 인덱스 (도형인 경우 0)
    public int Column { get; set; }           // 열 인덱스 (도형인 경우 0)
    public string RawValue { get; set; }      // 추출된 텍스트 원본 데이터
    
    // 비즈니스 파싱 결과 필드
    public DateTime? TargetDate { get; set; } // 연결된 스케줄 날짜 (역추적 또는 내부 파싱 결과)
    public string Content { get; set; }       // 일정 내용
    public string Instructor { get; set; }    // 파싱된 강사명
}
```

---

## 3. 핵심 파싱 비즈니스 로직 (Parser)

시트를 전수 조사하면서 일정을 만나면 **같은 열(Column)의 위쪽 행들을 역추적하여 날짜 마커를 획득**하는 알고리즘과 **도형 오브젝트 내부 텍스트**를 별도로 수집하는 핵심 구현부입니다.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.Json;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;

class ScheduleParser
{
    public static void ParseExcelSchedule(string filePath, string outputJsonPath)
    {
        // EPPlus 5 버전 이상 라이선스 컨텍스트 설정 (비상업적 이용 기준)
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        
        var serializedSchedules = new List<ScheduleItem>();

        using (var package = new ExcelPackage(new FileInfo(filePath)))
        {
            // 첫 번째 워크시트 선택
            var worksheet = package.Workbook.Worksheets[0]; 
            var dimension = worksheet.Dimension;

            if (dimension == null) return;

            // ==========================================
            // 1. 일반 셀 스캔 및 날짜 역추적 매핑
            // ==========================================
            for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
            {
                for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
                {
                    ExcelRange cell = worksheet.Cells[row, col];
                    if (cell.Value == null) continue;

                    string rawText = cell.Value.ToString().Trim();
                    if (string.IsNullOrEmpty(rawText)) continue;

                    // 1-1. 현재 셀 자체가 날짜 데이터 형태인지 판별
                    bool isDate = IsDateText(rawText, out DateTime parsedDate);

                    // 1-2. 핵심 로직: 현재 셀이 일반 일정인 경우 위쪽 행으로 거슬러 올라가며 날짜 탐색
                    DateTime? connectedDate = null;
                    if (isDate)
                    {
                        connectedDate = parsedDate;
                    }
                    else
                    {
                        // 현재 열(col)에서 현재 행(row)의 위쪽 방향으로 가장 가까운 날짜 셀 추적
                        connectedDate = FindNearestDateAbove(worksheet, row, col);
                    }

                    // 1-3. 텍스트 내부에서 강사명 등 부가 정보 필터링
                    string instructor = ExtractInstructor(rawText);

                    serializedSchedules.Add(new ScheduleItem
                    {
                        SourceType = ScheduleSource.Cell.ToString(),
                        Address = cell.Address,
                        Row = row,
                        Column = col,
                        RawValue = rawText,
                        TargetDate = connectedDate,
                        Content = isDate ? "날짜 마커" : rawText,
                        Instructor = instructor
                    });
                }
            }

            // ==========================================
            // 2. 격자 외부 도형(Shape) 내부 텍스트 추출
            // ==========================================
            foreach (ExcelDrawing drawing in worksheet.Drawings)
            {
                if (drawing is ExcelShape shape)
                {
                    string shapeText = shape.Text?.Trim();
                    
                    // 서식 있는 텍스트(RichText) 파싱 방어 코드
                    if (string.IsNullOrEmpty(shapeText) && shape.RichText != null)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var paragraph in shape.RichText) sb.Append(paragraph.Text);
                        shapeText = sb.ToString().Trim();
                    }

                    if (string.IsNullOrEmpty(shapeText)) continue;

                    // 도형 내부 텍스트 속에서 날짜 및 강사명 자체 파싱 유틸 작동
                    DateTime? shapeDate = ExtractDateFromText(shapeText);
                    string instructor = ExtractInstructor(shapeText);

                    serializedSchedules.Add(new ScheduleItem
                    {
                        SourceType = ScheduleSource.Shape.ToString(),
                        Address = shape.Name, // 도형 고유 오브젝트명
                        Row = 0,             // 격자 좌표계 영역 바깥이므로 0 처리
                        Column = 0,
                        RawValue = shapeText,
                        TargetDate = shapeDate,
                        Content = shapeText,
                        Instructor = instructor
                    });
                }
            }
        }

        // ==========================================
        // 3. 공용 활용을 위한 데이터 표준 JSON 직렬화
        // ==========================================
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true, 
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
        };
        
        string jsonOutput = JsonSerializer.Serialize(serializedSchedules, options);
        File.WriteAllText(outputJsonPath, jsonOutput);
    }

    // --- 비즈니스 로직 서포트 유틸리티 매서드 ---

    // [동작 알고리즘] 지정 행에서 위 방향으로 행인덱스만 감소시키며 가장 먼저 마주치는 날짜 셀을 반환
    private static DateTime? FindNearestDateAbove(ExcelWorksheet worksheet, int startRow, int col)
    {
        for (int r = startRow - 1; r >= worksheet.Dimension.Start.Row; r--)
        {
            var cellValue = worksheet.Cells[r, col].Value;
            if (cellValue != null)
            {
                if (IsDateText(cellValue.ToString(), out DateTime foundDate))
                {
                    return foundDate;
                }
            }
        }
        return null;
    }

    // 엑셀 서식 스타일에 따른 날짜 텍스트 파싱 처리 (상황에 맞춰 규칙 배열 추가 필요)
    private static bool IsDateText(string text, out DateTime parsedDate)
    {
        string[] formats = { "yyyy-MM-dd", "yyyy.MM.dd", "MM/dd", "M/d", "yyyy년 MM월 dd일" };
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
    }

    // 도형 텍스트 등 가공되지 않은 단일 통문자열 내에서 날짜 패턴을 정규식 등으로 찾아내는 메서드
    private static DateTime? ExtractDateFromText(string text)
    {
        // 비즈니스 룰 예시 (실제 텍스트 포맷에 매핑하는 정규식 작성부)
        if (text.Contains("9/11")) return new DateTime(2026, 9, 11); 
        return null;
    }

    // 문자열 본문에서 강사 식별 문자열을 추출하는 커스텀 메서드
    private static string ExtractInstructor(string text)
    {
        if (text.Contains("강사")) return "홍길동"; 
        return "미지정";
    }
}
```

---

## 4. 이 구조의 아키텍처적 이점

1. **셀 병합 및 레이아웃 제약 극복**
   `FindNearestDateAbove` 메서드가 위 방향 수직 탐색을 지원하므로, 날짜 행 밑으로 일정이 분할 나열되는 구조나 병합 처리된 변칙적 엑셀 폼에서도 데이터 손실 없이 정확히 날짜 값을 연결시킵니다.
2. **도형(Shape)의 공용 개체화**
   좌표 바깥에 유동적으로 배치된 텍스트 도형 요소들을 누락하지 않고 `SourceType = "Shape"` 상태 식별자로 함께 수집하여 후속 데이터 정제 엔진으로 유연하게 넘겨줄 수 있습니다.
3. **독립적인 이종 플랫폼 연동**
   최종 가공 테이블을 `System.Text.Json` 표준 규격으로 인코딩하여 즉시 떨구므로, 웹 API 응답 객체나 중앙 DB 저장용 원천 소스로 곧바로 호환됩니다.