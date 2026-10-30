# 7. 자동화·현장 측정

NexysBus는 명령줄 인자로 "자기 점검(셀프테스트)"을 돌리고 결과를 `gates.txt`(관문마다 PASS/FAIL + 숫자)로 남깁니다. 같은 절차를 매번 똑같이 반복하거나, 현장에서 결과를 그대로 가져오기 위한 기능입니다.

## 7.1 셀프테스트
| 명령 | 하는 일 |
|---|---|
| `NexysBus.exe --bus --out <폴더>` | 사용자 경로 전체: 새 프로젝트 → ICD 가져오기 → 프레임 편집 → 가상 실행 → ANALYZE → 내보내기 (15관문) |
| `--v100rc --out <폴더> [--xlsx <파일>]` | ANALYZE·ICD 상세 관문(50) |
| `--v0100 --out <폴더> [--truth <mon>]` | 100만 레코드 성능 |
| `--stress --iter 50` | 반복 실행 안정성 |
| `--hw --out <폴더> [--card n] [--channel n] [--seconds s]` | 하드웨어 카드 기본 점검(열거 → 열기 → 구성 → 실행 → 타임태그 → 무응답 RT → 캡처 파일) |
| `--site --out <폴더> --profile <json>` | **현장 측정**(아래) |
| `--icdxml --dev <장치 XML> --par <파라미터 XML> --truth <icd.csv>` | 장비 설정 파일 가져오기가 기준 ICD와 같은지 |
| `--shot --rail <레일> [--click <선택자>] [--js @파일] --name <이름>` | 화면 캡처(문서·영상 자동화용) |

## 7.2 현장 측정 `--site`
실제 RT가 있는 버스에서 다음을 숫자로 판정합니다.

| 단계 | 내용 | 관문 예 |
|---|---|---|
| H0 | 바인딩·카드 열거·열기·채널 능력·역할 배정 | 카드 ≥ 1 |
| H1 | 프레임 송신(`--seconds`) | 명령 수 = Σ 주기 ±1 %, 응답률 ≥ 99 %, 상태어 = 주소 << 11, 버스, Δt = 주기 ±1 %, DATA COUNT 0, 모드 코드 16/17, 기대 워드, 카운터 워드 |
| H2 | 다른 버스로 바꿔서(`--bseconds`) 후 복귀 | 응답 여부·복귀 |
| H3 | 장시간(`--minutes`) | 분당 메시지, 무응답 구간, 메모리 ≤ 50 MB |

```
NexysBus.exe --site --out results\SITE --profile my-bench.json --seconds 30 --bseconds 30 --minutes 10 --poll 5 --retain 0
```
프로필 없이 돌리면 H0(카드 확인)만 하고 버스에는 아무것도 보내지 않습니다.

### 사이트 프로필
현장마다 다른 값(RT 주소, 버스, 메시지, 기대 워드, 카운터)을 코드가 아니라 JSON 파일에 둡니다. 스키마: [site-profile.md](../site-profile.md), 예시: `app/site-profiles/example.json`.
```json
{
  "name": "my-bench", "rt": 4, "bus": "B", "periodMs": 20, "monitorChannel": 1,
  "messages": [
    { "sa": 1, "tr": 0, "wc": 15, "source": { "kind": "sine", "value": 1000, "amplitude": 500, "periodS": 2 } },
    { "sa": 3, "tr": 1, "wc": 30 },
    { "modeCode": 16, "tr": 1, "periodMs": 100 }
  ],
  "expect": { "3": { "1": "0081" } },
  "counter": { "sa": 4, "word": 1, "msPerTick": 10 }
}
```
결과 폴더: `gates.txt`, `selftest.log`, `calllog.txt`(드라이버 호출 순서), `h1_bc.mon` 등 캡처, `h1_sa_values.txt`(초당 수신 워드 — 상대 장비 화면과 대조용), `site_summary.json`.

## 7.3 현장 키트 절차 요약
1. 키트를 `C:\` 아래 공백 없는 경로로 복사, `driver.json` 작성.
2. 상대 RT가 어느 버스에 있는지 모르면 `hw_scan_rt.py`로 먼저 찾기.
3. `--site --profile …` 실행 → `gates.txt` 확인.
4. 수집 스크립트로 결과를 zip으로 묶어 가져오기.

다음: [8. 파일·단축키·문제 해결](08-reference.md)
