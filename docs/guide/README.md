# NexysBus 사용 가이드

NexysBus는 **MIL-STD-1553 버스를 소프트웨어로 돌리고**(BC 프레임 + 시뮬레이트 RT + 모니터), **캡처를 열어 값으로 풀어 보고**, **ICD**(메시지 · 워드 정의)를 다루는 Windows 프로그램입니다. 카드가 없어도 가상 버스로 전부 해 볼 수 있고, 카드가 있는 PC에서는 같은 화면으로 실제 버스에 붙습니다.

## 무엇부터 볼까요?

| 나는… | 여기부터 |
|---|---|
| 카드 없이 **1553 버스를 돌려 보고** 값을 보고 싶다 | **[카드 없이 버스 돌려 보기 →](quick-virtual.md)** (그림 따라 10분, 설치할 것 없음) |
| 받은 **캡처 파일(.mon)** 을 열어 보고 싶다 | **[캡처 파일 열어 보기 →](quick-capture.md)** (그림 따라 5분) |
| **실제 1553 카드**로 붙이고 싶다 | [6. 하드웨어 카드](06-hardware.md) — `driver.json` 한 개 + SEARCH HARDWARE |
| 모든 기능 · 설정 · 자동화 | 아래 "자세한 설명서" |

## 화면 한눈에

왼쪽 레일 5개만 기억하면 됩니다.

| 레일 | 하는 일 |
|---|---|
| **PROJECT** | 무엇으로 돌릴지(Virtual bus / 카드) · 버스 설정 |
| **ICD** | 메시지 · 워드 정의 — CSV · XLSX로 가져오기, 표에서 편집 |
| **SIMULATE** | 프레임(메시지 순서 · 주기) 실행 — START / STOP, 10 s BATCH |
| **ANALYZE** | 메시지 목록 · 디코드 · 그래프 · 통계 · CSV |
| **SETTINGS** | 테마 · 시간 표시 · 폴더 |

![ANALYZE](images/b6_analyze.png)

## 자세한 설명서

| 장 | 내용 | 이럴 때 |
|---|---|---|
| [1. 시작하기](01-getting-started.md) | 설치, 첫 실행, 화면 구성 | 처음 켰을 때 |
| [2. PROJECT](02-project.md) | 프로젝트 파일, 장치 선택, 버스 설정, 채널 능력 | 무엇으로 어떤 버스를 돌릴지 정할 때 |
| [3. ICD](03-icd.md) | 가져오기(CSV · XLSX · JSON · XML · 장치 설정 XML), 편집 · 검증, 내보내기 | 메시지 · 워드 정의를 만들거나 옮길 때 |
| [4. SIMULATE](04-simulate.md) | 프레임 표, 주기와 마이너/메이저 프레임, RT 데이터 소스, 실행 · BATCH, 오류 주입 | 버스를 돌릴 때 |
| [5. ANALYZE](05-analyze.md) | 캡처 목록, 필터 · 프리셋, 디코드, 추이, 통계, CSV | 결과를 볼 때 |
| [6. 하드웨어 카드](06-hardware.md) | driver.json, SEARCH HARDWARE, 단일/다중 기능 채널, 실측 결과 | 실제 카드로 붙일 때 |
| [7. 자동화 · 현장 측정](07-automation.md) | 명령줄 셀프테스트, 사이트 프로필, 현장 절차 | 반복 검증 · 현장 측정 |
| [8. 파일 · 단축키 · 문제 해결](08-reference.md) | 파일 형식, 키보드, 자주 묻는 것 | 막혔을 때 |

## 꼭 알아 둘 것
- 카드 없이 쓰는 데는 **설치할 것이 없습니다**(Virtual bus). 카드를 쓸 때만 카드 제조사 드라이버가 설치된 PC와 `driver.json`이 필요합니다.
- 항공 네트워크(이더넷 가상 링크) 도구는 별도 앱 **[NexysNet](https://github.com/Kim-Hakseong/nexys-net)** 입니다.
- 앱 안에서 **F1** = 도움말. 문제는 [GitHub Issues](https://github.com/Kim-Hakseong/nexys-bus/issues)로.
