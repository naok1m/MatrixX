# OCR Setup — Tesseract

O MatrixX usa o Tesseract 5 para ler o nome da arma do HUD. Você precisa do arquivo
de idioma antes do primeiro uso.

## Setup (uma vez só)

### 1. Baixe o `eng.traineddata`

Use o `tessdata_fast` (mais rápido, precisão idêntica ao `tessdata` padrão para fontes HUD):

> https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata

(~5 MB)

### 2. Coloque em `tessdata/` na raiz do repo

```
MatrixX/
├── src/
├── tests/
├── tessdata/
│   └── eng.traineddata       ← aqui
└── InputBusX.sln
```

O `.csproj` já está configurado para copiar essa pasta pro output em build/publish
(`CopyToOutputDirectory=PreserveNewest`), então a pasta `tessdata` vai aparecer
junto do `.exe` automaticamente.

### 3. Runtime do Visual C++

O binário nativo do Tesseract depende do **Visual C++ 2015-2022 x64 runtime**:

> https://aka.ms/vs/17/release/vc_redist.x64.exe

Máquinas com Windows 10/11 atualizadas normalmente já têm. Se der erro tipo
"DllNotFoundException: leptonica-1.x.dll" na inicialização, é isso.

## Debug do OCR

Se a leitura estiver ruim:

1. Marque `DebugSaveImages = true` em `WeaponDetectionSettings` (via `settings.json`
   ou via UI quando eu expuser o toggle).
2. Rode uma captura de teste.
3. Abra `%TEMP%\matrixx-ocr-debug\` — cada frame gera 4 arquivos:
   - `HHMMSS.fff_capture.png` — o raw da tela, antes de qualquer processamento
   - `HHMMSS.fff_lightOnDark_confXX.png`
   - `HHMMSS.fff_darkOnLight_confXX.png`
   - `HHMMSS.fff_lightOnDarkAggressive_confXX.png`

O número `confXX` é a confiança média do Tesseract pra aquela variante (0-100).
Se todas estão abaixo de ~60, a região capturada provavelmente está mal ajustada
— tente dar mais espaço na horizontal e um pouco de margem em cima e embaixo.

Depois de diagnosticar, **desligue `DebugSaveImages`** — cada captura grava 4 PNGs
e enche o disco rápido em detecção contínua.

## Por que trocou do Windows.Media.Ocr pro Tesseract?

O Windows.Media.Ocr é bonzinho pra texto em foto/documento, mas:

- Não tem controle sobre whitelist de caracteres → ele insiste em mapear "0" pra "O"
  dentro de palavras mesmo quando é claramente um dígito.
- Não expõe PageSegMode → sempre tenta achar múltiplas linhas e parágrafos.
- Engine fechada, sem ajuste de modelo.

O Tesseract 5 (LSTM) com `PSM=SingleLine` + whitelist + dicionário desligado é
literalmente a combinação pra esse caso de uso: uma linha, sem palavras de
dicionário, charset restrito.
