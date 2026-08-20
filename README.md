<div align="center"><img width="1296" height="718" alt="open" src="https://github.com/user-attachments/assets/e46a2086-6e97-4feb-a333-7a2794110b9e" width="80%"/></div>

# <div align="center">Mort en Bourdonnant 

**<div align="center">파리대왕의 명을 받아 인간의 집에 스며드는 협동 생존 게임**

<br/>

<!-- ⚙️ 배지는 실제 엔진/언어/네트워킹에 맞게 수정하세요 -->
![Engine](https://img.shields.io/badge/Engine-Unity-34507E?logo=unity&logoColor=white)
![Language](https://img.shields.io/badge/C%23-161E32?logo=csharp&logoColor=white)
![Players](https://img.shields.io/badge/Players-2~4-E0B030)
![Mode](https://img.shields.io/badge/Online-Multiplayer-2DC4C4)
![Genre](https://img.shields.io/badge/Genre-Lethal--like-E05555)
![Status](https://img.shields.io/badge/Status-Demo-2ea043)



[<img src="Assets/UI/pheromone.png" width="24" align="top"/> 시연 영상 ](youtube.com/watch?v=9QQarRtKDgo&source_ve_path=MjM4NTE&embeds_referring_euri=https%3A%2F%2Fcafe.naver.com%2F)
[<img src="Assets/UI/dash.png" width="24" align="top"/> 최종 발표 슬라이드](https://docs.google.com/presentation/d/1ZB4qu6_4U1Y3EyLE2RibS6REm2xDPt53/edit?slide=id.p5#slide=id.p5)
</details>

<img src="assets/gameplay.gif" width="450"/>

</div>

---

## 📑 목차

- [게임 소개](#-게임-소개)
- [스토리](#-스토리)
- [핵심 특징](#-핵심-특징)
- [등장하는 적](#-등장하는-적)
- [구현 기능](#-구현-기능)
- [기술 스택](#-기술-스택)
- [팀 소개](#-팀-소개)

---

## <img src="Assets/UI/heart.png" width="24" align="top"/> 게임 소개
<img width="514" height="290" src="https://github.com/user-attachments/assets/be3f06e4-5799-4a88-b075-d583e6d5f7bb" />


> 한 줄 컨셉: **"파리가 되어 인간의 집에 침투해 음식을 먹고 알을 바치는 협동 멀티플레이 생존 게임"**

**Mort en Bourdonnant**은 리썰 컴퍼니류(Lethal-like)의 협동 생존 게임입니다.
플레이어는 파리가 되어 인간의 집에 잠입하고, 음식을 먹어 유충(알)을 만들어 파리대왕에게 바칩니다.
고양이와 거미가 도사리는 집을 동료와 함께 헤쳐 나가는 **2~4인 협동 플레이**에 초점을 맞췄습니다.

<sub> 서경대학교 게임프로그래밍 수업 · 4조 팀 프로젝트</sub>

---

## 📜 스토리

파리대왕들이 파리들에게 **알을 바치라** 명합니다.
명을 받은 파리(플레이어)들은 직접 인간의 집에 들어가 음식을 먹고, 유충을 만들어 바쳐야 합니다.
하지만 집 안에는 파리를 노리는 위협이 곳곳에 도사리고 있습니다.

---

##  <img src="Assets/UI/money.png" width="24" align="top"/> 핵심 특징
<img width="514" height="290" alt="egg" src="https://github.com/user-attachments/assets/9aff6914-9cad-4b45-a531-f62fadb7e4d9" />

### 🍖 '허기' 시스템 — 채워지는 스탯

맵에 음식을 먹고 허기게이지를 채우면 알을 그자리에서 낳아버립니다. 
플레이어는 그 알을 기지까지 안전히 수거해가야합니다. 

<img width="514" height="290" alt="jump" src="https://github.com/user-attachments/assets/9036da81-d380-4131-ba13-3f65c58cc702" />

### 🤝 2~4인 협동 멀티플레이

혼자가 아니라 동료와 함께. 멀티플레이를 중심으로 게임의 재미를 설계했습니다.

<img width="514" height="290" alt="screenshot_play" src="https://github.com/user-attachments/assets/4f8c0f12-bcaf-409e-9198-1dd2bc54213c" />

### 🗺️ 절차적 맵 생성

**랜덤 시드** 기반으로 매 플레이마다 방 구성이 달라져 반복 플레이의 재미를 더합니다.

<img width="514" height="290" alt="MedalTVMultiplayTestWithUnity20260611224552049" src="https://github.com/user-attachments/assets/8f188eff-be28-40d0-964f-8ff07aa831df" />

### 🔥 매판 늘어나는 할당량

라운드마다 급격히 늘어나는 할당량의 채우기 위해 플레이어들은 위험을 무릅쓰고 집에 들어갑니다. 
모든 플레이어가 사망하거나 할당량을 채우지 못할 경우 
파리대왕의 제물로 바쳐져 **게임오버**가 됩니다. 

---

## 🐾 등장하는 적

🐱 **고양이**  

- 맵을 배회하다 플레이어를 감지
- 감지 시 **플레이어의 높이에 따라** 다른 공격을 가함

<img width="514" height="290" alt="cat" src="https://github.com/user-attachments/assets/456a46ce-fe59-46d0-b1a9-12a2ea95d106" width="450"/>

🕷️ **거미** 

- 맵을 배회하며 **거미줄**을 생성
- 거미줄에 닿은 플레이어를 감지해 **추적** 

<img width="514" height="290" alt="spider" src="https://github.com/user-attachments/assets/2f329a71-5505-48db-830e-6b5c24ce96e6" width="450"/>


---

## <img src="Assets/UI/aim_defaultaim.png" width="24" align="top"/> 조작 방법

| 동작 | 키 |
| :--- | :---: |
| 이동 | `W` `A` `S` `D` |
| 비행 | `R` |
| 달리기 | `Shift` |
| 상호작용 | `F` |
| 대쉬 / 탐색 | `G` `R_clk` |
| 점프 | `Space` |


---
## <img src="Assets/UI/aim_Food.png" width="24" align="top"/> 구현 기능

- [x] 2~4인 협동 멀티플레이 (네트워킹)
- [x] 플레이어 조작, 스킬
- [x] 인게임 UI
- [x] 적 AI — 고양이, 거미
- [x] 랜덤 시드 기반 방 생성, 랜덤 음식 생성
- [x] 거리별 공간 음향 조절
- [x] 코어 루프 완성

## <img src="Assets/UI/skill_able.png" width="24" align="top"/> 기술 스택

- **엔진**: Unity 6000.3.12f1 (또는 사용한 버전)
- **언어**: C#
- **버전 관리**: Git, GitHub

네트워크 & 멀티플레이어
| 패키지 | 버전 | 용도|
| :--- | :---: | :---:|
| Netcode for GameObjects | 2.11.0	| 멀티플레이어 네트워킹|
| Unity Multiplayer Tools	| 2.2.8 |	네트워크 디버깅|
| Unity Services Vivox	| 16.10.0	|음성 채팅 (포지셔널 보이스)|
| ParrelSync	| -	| 멀티 에디터 동시 테스트|

랜더링 & 그래픽
| 패키지 | 버전 | 용도|
| :--- | :---: | :---:|
|Universal Render Pipeline (URP)	|17.3.0	|렌더링 파이프라인|
|Shader Graph	|17.3.0	|커스텀 셰이더|
|Post Processing	|3.5.4	|후처리 효과|

게임플레이 & AI
| 패키지 | 버전 | 용도|
| :--- | :---: | :---:|
|AI Navigation (NavMesh)	|2.0.11	몬스터 |AI 길찾기|
|Input System	|1.19.0	플레이어 입력 처리|
|Timeline	|1.8.11	|연출 / 시퀀스|
|ProBuilder	|6.0.9	|인게임 맵 모델링|


---

## <img src="Assets/UI/heart.png" width="24" align="top"/> 팀 소개

**게임프로그래밍 4조**

| 이름 | 역할 | GitHub |
| :---: | :--- | :---: |
| 도윤재 | 기획 / 조작  | [@Do-00](https://github.com/Do-00) |
| 유동현 | 네트워킹/시스템 | [@ontip11](https://github.com/ontip11) |
| 최유빈 | UI / 애니메이션 | [@youbin999](https://github.com/youbin999) |

---


## 📄 라이선스

이 프로젝트는 학습/포트폴리오 목적으로 제작되었습니다.

---

<div align="center">
<sub> Made with buzz by 4조 · 게임프로그래밍</sub>
</div>
