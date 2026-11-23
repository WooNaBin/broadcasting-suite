## 엑셀 문자열 데이터 예시

분석된 문자열의 타입 분류 참고 : [ScheduleDataTypeScript.md](Excel/RefDoc/ScheduleDataTypeScript.md)

```C#
        public class ScheduleData
        {
            //... 생략
            public ScheduleTime scheduleDate;
            public ScheduleTime releaseDate;
            public int startTime;
            public string scheduleTitle;
            public string accessoryInfo;
            public Place place;
            public string sermonSpeaker;
            public Preparation preparation;
            public string preaparationNote;
            //... 생략
        }   
```

### 분류된 예시 구분

- {제목} : 스케쥴 Data의 일정 제목. `ScheduleData.scheduleTitle`
- {날짜} : 스케쥴 Data의 일정 날짜. `ScheduleData.scheduleDate` or `ScheduleData.scheduleDate` 포함
- {강사} : 스케쥴 Data의 일정 강사. `ScheduleData.sermonSpeaker`
- {기타} : 스케쥴 Data의 일정 강사. `ScheduleData.accessoryInfo`



### A.한 줄 형태 (총 5개의 예시)

```
교구집회(8.31-9.4) : 매곡6(이정익P),첨단2(이기천P),용봉3(김선욱P),두암(양현준P)
청년회집회(7-12):정진관P  /  담양고서(7-11):이태훈P  /  직장선교부_서부유통&광일(7-11):박종욱P
추계수양회(17-20)
초등부집회(8.7-9) : 이주현P
대전도집회(5.11-17) : 임승환P      /    초등부(김규석E, 성재경E, 송경근E)
```



### B.두 줄 형태 (아래의 내용이 한 번에 두 줄의 내용으로 한꺼번에 올 수 있음)

```
담양고서집회(박경수P)
교구집회:동림(강대석P).삼각1(김도형P),매곡5(이주현P),신용3(권오진P)
```


## 문자열 데이터의 분석결과


| 입력 문자열 | 분석딘 분류 타입 |
| --- | --- |
|교구집회(8.31-9.4) : 매곡6(이정익P),첨단2(이기천P),용봉3(김선욱P),두암(양현준P)| {제목}({날짜}) : {기타}({강사}),{기타}({강사}),{기타}({강사}),{기타}({강사}) |
|청년회집회(7-12):정진관P  /  담양고서(7-11):이태훈P  /  직장선교부_서부유통&광일(7-11):박종욱P|{제목}({날짜}):{강사}  /  {제목}{날짜}:{강사}  /  {제목}{날짜}:{강사}|
|추계수양회(17-20)|{제목}({날짜})|
|초등부집회(8.7-9) : 이주현P|{제목}{날짜} : {강사}|
|대전도집회(5.11-17) : 임승환P      /    초등부(김규석E, 성재경E, 송경근E)|{제목}{날짜} : {강사}      /    {제목}{강사}|



