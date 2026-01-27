# agents.md — Online Ensemble Client (Networking MVP)

## 0. 목적 (MVP 범위 고정)

이 프로젝트의 단기 목표는 **서버와의 통신이 동작하는 Unity 클라이언트 골격**을 만드는 것이다.  
컨텐츠/게임플레이/UI를 채우는 작업은 하지 않는다.

MVP에서 반드시 구현할 것:

1. **TCP 기반 gRPC Echo 서버**와 통신 (요청/응답)
2. **UDP Echo 서버**와 통신 (송신/수신)
3. 이를 수용하는 최소 구조:
   - `Bootstrap` 씬
   - `GameManager`
   - `NetworkManager`(향후 확장 가능한 전신 형태)

MVP에서 하지 않을 것:

- 매칭, 로비, 룸, 오디오 스트리밍, 리듬게임, 데이터 저장, 인증, UI 시스템 구축
- Addressables, ECS, DI 프레임워크 도입 등 과한 인프라 확장

---

## 1. 개발 원칙

- “작동하는 최소 구현” + “구조적 확장성”을 동시에 만족한다.
- Unity 메인 스레드 제약을 준수한다:
  - 네트워크 수신은 백그라운드에서 수행 가능
  - Unity 오브젝트 접근/상태 반영은 메인 스레드에서만 수행
- 네트워크 계층은 **Transport 레벨**과 **Manager 레벨**을 분리한다.
- 로그는 문제 재현을 위해 필수이며, 연결/송수신/종료의 모든 경로에서 남긴다.
- 코드는 향후 기능 추가(로비/룸/실시간 입력/오디오)를 위해 재사용 가능해야 한다.

---

## 2. 목표 아키텍처 (최소 형태)

### 씬

- `Assets/Scenes/Bootstrap.unity`
  - 게임 시작 시 자동 로딩되는 씬
  - `Bootstrapper` 오브젝트 1개만 존재
  - 여기서 `GameManager`를 생성하고 영구 유지(`DontDestroyOnLoad`)한다.

### 런타임 오브젝트

- `GameManager` (MonoBehaviour, Singleton-like)
  - 앱 레벨 상태 소유
  - `NetworkManager`를 소유/수명 관리
  - 연결 상태, 마지막 에코 응답, 마지막 오류 등을 보관
  - 개발 편의용으로 키 입력 또는 Inspector 버튼으로 “Echo 보내기”가 가능해도 됨(과한 UI 금지)

- `NetworkManager` (MonoBehaviour)
  - TCP(gRPC) / UDP 각각을 하위 컴포넌트(Transport)로 위임
  - 수신 이벤트는 메인 스레드 큐로 디스패치하여 GameManager에 전달
  - 재연결/에러/종료 경로를 명확히 구분

### Transport 레이어

- `ITransport` 인터페이스
  - ConnectAsync / DisconnectAsync / IsConnected / SendAsync / Poll(또는 이벤트)

- `GrpcEchoClientTransport`
  - gRPC Echo 호출(예: Echo(string) -> EchoReply(string))
  - 재시도는 MVP에서는 과도하므로 1회 호출 실패 시 에러 반환만

- `UdpEchoTransport`
  - UDP send/receive loop
  - Receive는 백그라운드에서 수행하고 결과를 thread-safe queue에 push

---

## 3. 네트워크 요구사항 (MVP)

### TCP(gRPC) Echo

- 서버 주소는 구성 파일(예: ScriptableObject 또는 JSON)로 관리
- 클라이언트는 문자열을 보내고 문자열 응답을 받는다
- 최소 1개 RPC:
  - `Echo` (request: string message, response: string message)

주의:

- Unity에서 gRPC는 런타임/플랫폼(IL2CPP 포함) 이슈가 있을 수 있으므로,
  **MVP 1차는 Editor/Standalone(Mono 또는 .NET 호환 환경)에서 동작 보장**을 우선한다.
- IL2CPP/모바일 대응은 MVP 이후 단계로 미룬다.

### UDP Echo

- 서버 IP/Port로 UDP 패킷을 송신
- 동일 payload를 echo로 수신하면 성공으로 간주
- 수신은 타임아웃(예: 1~3초)을 두고 실패 시 에러로 기록

---

## 4. 파일/폴더 구조 (필수)

다음 경로를 준수해 생성한다.

- `Assets/Scenes/Bootstrap.unity`
- `Assets/Scripts/Bootstrap/Bootstrapper.cs`
- `Assets/Scripts/Core/GameManager.cs`
- `Assets/Scripts/Network/NetworkManager.cs`
- `Assets/Scripts/Network/Transports/ITransport.cs`
- `Assets/Scripts/Network/Transports/GrpcEchoClientTransport.cs`
- `Assets/Scripts/Network/Transports/UdpEchoTransport.cs`
- `Assets/Scripts/Network/Model/NetworkConfig.cs` (ScriptableObject 권장)
- `Assets/Scripts/Utils/MainThreadDispatcher.cs` (ConcurrentQueue 기반)

---

## 5. 설정(Config) 규칙

- 서버 주소/포트는 코드에 하드코딩하지 않는다.
- `NetworkConfig` ScriptableObject를 만들고,
  - gRPC endpoint (host, port, useTls)
  - UDP endpoint (host, port)
    를 Inspector에서 설정 가능하게 한다.
- `Bootstrapper`가 `NetworkConfig`를 참조하며, 시작 시 이를 GameManager에 전달한다.

---

## 6. 동작 시나리오 (Acceptance Criteria)

아래 시나리오가 Unity Editor Play에서 재현 가능해야 한다.

1. Play 버튼을 누른다
2. Bootstrap 씬이 로딩되고 GameManager/NetworkManager가 생성된다
3. 자동 또는 수동 트리거로:
   - gRPC Echo: "hello-grpc" 전송 → 응답 수신 → Console 로그에 출력
   - UDP Echo: "hello-udp" 전송 → 응답 수신 → Console 로그에 출력
4. 네트워크 종료(Play 중지 또는 OnDestroy) 시:
   - 소켓/클라이언트가 정상 해제되고 예외가 발생하지 않는다

필수 로그 예시(형식은 자유):

- `[NET][GRPC] Connect ...`
- `[NET][GRPC] Echo request: ...`
- `[NET][GRPC] Echo response: ...`
- `[NET][UDP] Send ...`
- `[NET][UDP] Receive ...`
- `[NET] Disconnect ...`
- 에러 발생 시: 예외 메시지 + 스택(가능하면) + endpoint 정보

---

## 7. 코딩 규칙

- C# nullable 경고를 고려한 방어 코드 작성
- 예외는 삼키지 말고 로그를 남긴 뒤 상위로 오류 상태를 전달
- 네트워크 수신 스레드/Task에서 Unity API 호출 금지
- NetworkManager는 “God Object”가 되지 않도록:
  - 연결/송수신 구현은 Transport로 위임
  - NetworkManager는 조율/큐 디스패치/상태 관리에 집중

---

## 8. 구현 순서 (Codex 작업 지시)

Codex는 아래 순서로 작업한다.

1. Bootstrap 씬 생성 + Bootstrapper 배치 + 시작 시 GameManager 생성
2. GameManager / NetworkManager 스켈레톤 작성
3. MainThreadDispatcher 구현(ConcurrentQueue + Update 디스패치)
4. NetworkConfig ScriptableObject 생성 및 Bootstrapper 연결
5. UDP Echo Transport 구현 및 성공 로그 확인
6. gRPC Echo Transport 구현(필요 패키지/프로토 생성 포함)
7. 간단한 트리거 제공:
   - 자동 1회 송신 또는 Inspector 버튼/키 입력 중 1개 선택
8. 종료/정리 루틴 확실히 처리(OnDestroy/OnApplicationQuit)

---

## 9. gRPC 관련 지침 (중요)

- 가능한 한 “공식 .NET gRPC 클라이언트” 방식에 맞춘다.
- `.proto`는 `Assets/Proto/echo.proto` 같은 경로로 관리한다.
- 코드 생성 산출물은 `Assets/Scripts/Generated/` 아래로 둔다(또는 별도 폴더).
- Unity 환경 제약으로 인해, 만약 HTTP/2 제약/런타임 충돌이 발생하면:
  1. Editor/Standalone에서 먼저 성공시키고
  2. 실패 원인(런타임/플랫폼/패키지)을 로그로 정리한다
  3. MVP 범위 내에서 가능한 우회(예: 플랫폼 제한 명시)만 적용한다

---

## 10. “완료”의 정의

다음이 충족되면 이번 에이전트 작업은 완료다.

- Bootstrap 씬만으로 실행 가능
- GameManager가 NetworkManager를 소유
- UDP Echo 성공 로그가 1회 이상 확인됨
- gRPC Echo 성공 로그가 1회 이상 확인됨
- Play 종료 시 오류/예외 없이 정리됨

(부가 작업은 하지 않는다. 요구되지 않은 기능 확장은 금지)
