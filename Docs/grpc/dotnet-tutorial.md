# gRPC-Web for .NET (Unity 클라이언트) 최소 튜토리얼

이 문서는 Microsoft Learn의 **gRPC for .NET 지원 플랫폼** 공식 문서를 기준으로,  
Unity에서 **gRPC-Web** 방식으로 unary 호출을 수행하는 **가장 단순한** 사용 흐름을 정리한 프로젝트 규격 문서다.

- 목적: Unity 환경에서 HTTP/2 제약을 우회하고 gRPC-Web으로 unary 호출 수행
- 범위: `GrpcChannel.ForAddress → new Client → unary call`
- 제외: DI, ClientFactory, ASP.NET Core 서버 패턴 상세, 인터셉터/리트라이/밸런싱

---

## 1. 배경 (Unity + gRPC 제약)

- gRPC(정석 grpc-dotnet)는 **HTTP/2**가 필수다.
- Unity 환경은 기본 HTTP/2 지원이 부족하거나 불완전해 **Grpc.Net.Client가 정상 동작하지 않는 경우가 많다**.
- 따라서 Unity는 **gRPC-Web** 사용이 권장된다.

---

## 2. gRPC-Web 핵심 제약

- **지원되는 호출 타입**: Unary, Server Streaming
- **지원되지 않는 호출 타입**: Client Streaming, Bi-Directional Streaming
- 서버는 gRPC-Web 요청을 **직접 처리**하거나 **프록시(예: Envoy)**로 변환되어야 한다.

---

## 3. 최소 패키지 요구 사항

- `Grpc.Net.Client`
- `Grpc.Net.Client.Web`
- `Grpc.Net.Common`
- `Grpc.Core.Api`
- `Google.Protobuf`

---

## 4. 최소 구조 (예시)

```
Assets/
  _Project/
    Scripts/
      TcpClient/
        Echo.cs        (protobuf 생성 코드)
        EchoGrpc.cs    (gRPC 서비스 스텁 생성 코드)
```

`EchoGrpc.cs`의 `EchoServiceClient`를 사용한다.

---

## 5. 프로토콜 정의 (예시)

```proto
syntax = "proto3";

package echo;

service EchoService {
  rpc Echo (EchoRequest) returns (EchoReply);
}

message EchoRequest {
  string message = 1;
}

message EchoReply {
  string message = 1;
}
```

---

## 6. Unity gRPC-Web 클라이언트 최소 코드

### 6.1 GrpcWebHandler + GrpcChannel 생성

```csharp
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Echo;
using UnityEngine;

public class GrpcWebEchoSample : MonoBehaviour
{
    public string ip = "127.0.0.1";
    public int port = 50051;

    public async Task SendHello()
    {
        // gRPC-Web handler를 HttpClient 파이프라인에 연결
        var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler());
        var httpClient = new HttpClient(grpcWebHandler);

        var address = $"https://{ip}:{port}";
        using var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        var client = new EchoService.EchoServiceClient(channel);

        try
        {
            var reply = await client.EchoAsync(new EchoRequest { Message = "Hello" });
            Debug.Log($"Echo reply: {reply.Message}");
        }
        catch (Grpc.Core.RpcException ex)
        {
            Debug.LogError($"gRPC-Web call failed: {ex.Status}");
        }
    }
}
```

---

## 7. GrpcWebMode 선택 규칙

- `GrpcWebMode.GrpcWeb`  
  - 일반적인 gRPC-Web (권장 기본값)
- `GrpcWebMode.GrpcWebText`  
  - 서버/프록시가 **grpc-web-text**만 지원하는 경우 사용

---

## 8. 서버 요구 사항 (요약)

- 서버는 **gRPC-Web 요청을 수락**해야 한다.
- 지원 방법:
  - gRPC-Web 직접 지원
  - gRPC-Web ↔ gRPC 변환 프록시 사용

---

## 9. 실행 순서 요약

1. `.proto` 작성 및 C# 코드 생성
2. Unity에서 `GrpcWebHandler` 생성
3. `GrpcChannel.ForAddress(...)` 생성
4. `new EchoServiceClient(channel)`
5. `client.EchoAsync(...)` 호출
6. 응답 처리 및 실패 로그 출력

---

## 10. 운영 규격 (프로젝트 기준)

- Unity는 기본적으로 **gRPC-Web만 허용**한다.
- **Unary 호출만** 기본 지원한다.
- 서버가 gRPC-Web을 지원하지 않으면 연결 불가로 간주한다.
- 실패 로그는 `RpcException.Status` 중심으로 기록한다.

---

끝.
