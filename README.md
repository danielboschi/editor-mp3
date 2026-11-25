# Editor de MP3

Programa simples para Windows que corta o começo e o fim de arquivos de áudio MP3 e WAV. Você pode ouvir como o áudio ficou antes de salvar.

## Download

Baixe o `EditorMP3.exe` na página de [Releases](../../releases/latest).

É um executável **portátil**: basta copiar e abrir. Não precisa instalar o programa nem o .NET.
Funciona no Windows 10 e 11 de 64 bits.

> Na primeira vez, o Windows pode mostrar "O Windows protegeu o computador", porque o programa não tem assinatura digital.
> Clique em **Mais informações → Executar assim mesmo**.

## Como usar

1. **Abrir:** clique em "Abrir arquivo…" ou arraste um áudio para a janela. Para outros formatos, escolha "Todos os arquivos" na tela de abrir.
2. **Escolher o corte:**
   - Arraste a linha **verde** (início) e a **vermelha** (fim) na forma de onda.
   - Ou digite o tempo nas caixas (ex.: `01:23,500`) e aperte Enter.
   - Ou leve o cursor branco até o ponto e clique em "Marcar início aqui" / "Marcar fim aqui".
3. **Fade (opcional):** marque "Fade in no começo" e/ou "Fade out no final" e escolha a duração em segundos. A curva amarela no gráfico mostra o efeito.
4. **Ouvir antes de salvar:**
   - **Reproduzir / Pausar** toca o áudio a partir do cursor branco. A tecla **Espaço** faz o mesmo.
   - Clique ou arraste em qualquer ponto do gráfico para mover o cursor.
   - **Ouvir resultado** toca só o trecho que será salvo, com os fades.
   - **Primeiros 3 s** e **Últimos 3 s** servem para conferir as bordas do corte.
5. **Salvar como…:** o arquivo original nunca é alterado.

## Formatos

| Entrada → Saída | Como é feito |
|---|---|
| MP3 → MP3 | Corte **sem perda de qualidade**: o áudio não é codificado de novo. Precisão de ~26 ms. |
| WAV → WAV | Corte exato, amostra por amostra. |
| MP3 → WAV | Decodifica para WAV 16 bits. |
| WAV → MP3 | Codifica em MP3 (até 192 kbps) usando o codificador do Windows. |
| Outros (M4A, WMA…) → MP3 ou WAV | Converte usando os codecs do Windows. |

O fade em MP3 → MP3 também não recodifica: muda só o campo de volume dos blocos do trecho com fade, em passos de 1,5 dB.

O formato é reconhecido pelo conteúdo do arquivo, mesmo que a extensão esteja errada.
Formatos que o Windows não decodifica não abrem, e o programa avisa.

Em edições "N" do Windows, instale antes o *Media Feature Pack* da Microsoft.

## Compilar a partir do código

É preciso ter o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
# Executar em modo de desenvolvimento
dotnet run --project EditorMP3

# Testes automáticos de corte e da tela
dotnet run --project Tests\TrimTests
dotnet run --project Tests\UiTests

# Gerar o executável único e portátil em dist\
dotnet publish EditorMP3 -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist

# Gerar o ícone de novo
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

## Estrutura

- `EditorMP3/`: o aplicativo (C#, WPF, .NET 10)
  - `Audio/AudioTrimmer.cs`: lógica de corte e de salvar
  - `Audio/Fade.cs` e `Audio/Mp3FrameGain.cs`: fade in e fade out
  - `Audio/AudioSource.cs`: abertura e reconhecimento dos formatos
  - `Audio/PlaybackService.cs`: reprodução da prévia
  - `Controls/WaveformControl.cs`: forma de onda, linhas de corte e cursor
- `Tests/TrimTests/`: testes automáticos dos cortes
- `Tests/UiTests/`: testes automáticos da tela
- `tools/make-icon.ps1`: gera o ícone

Usa a biblioteca [NAudio](https://github.com/naudio/NAudio) (licença MIT).
