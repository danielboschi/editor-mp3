# Editor de MP3

Programa simples para Windows que corta o começo e o fim de arquivos de áudio MP3 e WAV. Você pode ouvir como o áudio ficou antes de salvar.

## Download

Baixe o `EditorMP3.exe` na página de [Releases](../../releases/latest).

É um executável **portátil**: basta copiar e abrir. Não precisa instalar o programa nem o .NET.
Funciona no Windows 10 e 11 de 64 bits.

> Na primeira vez, o Windows pode mostrar "O Windows protegeu o computador", porque o programa não tem assinatura digital.
> Clique em **Mais informações → Executar assim mesmo**.

## Como usar

1. **Abrir:** clique em "Abrir arquivo…" ou arraste um MP3/WAV para a janela.
2. **Escolher o corte:**
   - Arraste o marcador **verde** (início) e o **vermelho** (fim) na forma de onda.
   - Ou digite o tempo nas caixas (ex.: `01:23,500`) e aperte Enter.
   - Ou toque o áudio e clique em "Marcar início aqui" / "Marcar fim aqui" no momento certo.
3. **Ouvir antes de salvar:**
   - **Ouvir resultado** toca só o trecho que será salvo. A tecla **Espaço** também toca e pausa.
   - **Primeiros 3 s** e **Últimos 3 s** servem para conferir as bordas do corte.
   - **Ouvir original** toca o arquivo inteiro.
4. **Salvar como…:** o arquivo original nunca é alterado.

## Formatos

| Entrada → Saída | Como é feito |
|---|---|
| MP3 → MP3 | Corte **sem perda de qualidade**: o áudio não é codificado de novo. Precisão de ~26 ms. |
| WAV → WAV | Corte exato, amostra por amostra. |
| MP3 → WAV | Decodifica para WAV 16 bits. |
| WAV → MP3 | Codifica em MP3 192 kbps usando o codificador do Windows. |

Em edições "N" do Windows, a conversão WAV → MP3 precisa do *Media Feature Pack* da Microsoft.

## Compilar a partir do código

É preciso ter o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# Executar em modo de desenvolvimento
dotnet run --project EditorMP3

# Testes automáticos de corte
dotnet run --project Tests\TrimTests

# Gerar o executável único e portátil em dist\
dotnet publish EditorMP3 -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist

# Gerar o ícone de novo
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

## Estrutura

- `EditorMP3/`: o aplicativo (C#, WPF, .NET 8)
  - `Audio/AudioTrimmer.cs`: lógica de corte e de salvar
  - `Audio/PlaybackService.cs`: reprodução da prévia
  - `Controls/WaveformControl.cs`: forma de onda com os marcadores
- `Tests/TrimTests/`: testes automáticos dos cortes
- `tools/make-icon.ps1`: gera o ícone

Usa a biblioteca [NAudio](https://github.com/naudio/NAudio) (licença MIT).
