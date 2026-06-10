# Как снять реальные SHM-дампы из ACC (fixtures для тестов)

Разовая операция разработчика на Windows-машине с установленной ACC. Дампы коммитятся в эту
папку и навсегда становятся «эталонными байтами реальной игры» для layout-тестов — CI на любой
ОС проверяет наши структуры против них, а не только против синтетики. Пользователям приложения
ничего снимать не нужно: `AccSharedMemoryReader` читает shared memory сам в рантайме.

## Шаги

1. Запусти ACC, зайди в **практику** на любой трассе, выезжай на трек.
2. Проедь 1–2 поворота (чтобы physics-поля были «живыми»: скорость, передача, температуры).
3. Поставь игру на **паузу** (Esc) — страницы замёрзнут, дамп гарантированно не будет «порванным»
   (torn read).
4. Не выходя из игры, открой PowerShell и выполни:

```powershell
$pages = @{ physics = 800; graphics = 1588; static = 820 }
foreach ($name in $pages.Keys) {
    $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\acpmf_$name")
    $stream = $mmf.CreateViewStream(0, $pages[$name])
    $bytes = New-Object byte[] $pages[$name]
    $null = $stream.Read($bytes, 0, $bytes.Length)
    [System.IO.File]::WriteAllBytes("$PWD\acc_$name.bin", $bytes)
    $stream.Dispose(); $mmf.Dispose()
}
Get-ChildItem acc_*.bin
```

5. Проверь содержимое (размер файлов проверять бессмысленно — его задаёт сам скрипт):

```powershell
foreach ($name in 'physics', 'graphics') {
    $bytes = [System.IO.File]::ReadAllBytes("$PWD\acc_$name.bin")
    "{0}: packetId = {1}" -f $name, [BitConverter]::ToInt32($bytes, 0)
}
$static = [System.IO.File]::ReadAllBytes("$PWD\acc_static.bin")
"static: smVersion = " + [System.Text.Encoding]::Unicode.GetString($static, 0, 30).TrimEnd([char]0)
```

   `packetId` должен быть > 0, `smVersion` — начинаться с «1.» (например, `1.8`).
   Нули/пустые строки = страницы пустые, дамп снят вне сессии — не годится.

## Что записать вместе с дампом

Тесты будут assert'ить известные значения, поэтому зафиксируй контекст на момент снятия:

- машина (точное имя из меню, например Audi R8 LMS Evo II)
- трасса
- тип сессии (практика), погода (ясно/дождь), примерное время суток
- передача и примерная скорость в момент паузы (если помнишь)

## Куда положить

- Файлы — сюда: `tests/SimCoach.Adapters.ACC.Tests/Fixtures/`
- Контекст — сообщи агенту (или допиши сюда в раздел ниже), он добавит тесты
  `*_real_dump_*` с assert'ами на известные значения.

## Возможные проблемы

- `OpenExisting` бросает `FileNotFoundException` — ACC не запущена или ты ещё не вошёл
  в сессию (страницы создаются при входе в сессию, не на старте игры).
- PowerShell должен быть запущен от того же пользователя, что и игра (обычный случай);
  права администратора не нужны.

## Контекст снятых дампов

_(заполняется при снятии)_
