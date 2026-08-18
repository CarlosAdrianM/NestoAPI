# Renovar el certificado digital de la AEAT (VNifV2)

**Para qué sirve**: NestoAPI se autentica en el servicio VNifV2 de la AEAT (validación de
NIF contra el censo: facturación, circuito #327, autocurado #383) con el certificado FNMT
de **representante de persona jurídica** (Carlos / Nueva Visión, A78368255). Caduca cada
**2 años**. Cuando falten menos de 15 días, ELMAH avisa a diario ("CertificadoAeat: el
certificado de la AEAT caduca el...").

## El proceso (⏱ ~5 minutos + la espera de la FNMT)

### 1. Conseguir el certificado nuevo (esto es lo único que tarda: ~2 días)

1. Solicitar la renovación en la FNMT (https://www.sede.fnmt.gob.es/certificados/certificado-de-representante).
   Entre el Registro Mercantil y la FNMT suelen tardar un par de días.
2. Descargar el certificado en el navegador donde se solicitó y **exportarlo a un .pfx**:
   `Win+R` → `certmgr.msc` → Personal → Certificados → clic derecho sobre el nuevo →
   Todas las tareas → Exportar → **"Sí, exportar la clave privada"** ⚠️ (imprescindible) →
   siguiente, siguiente → ponerle una contraseña → guardarlo en **Descargas**.

### 2. Ejecutar UN comando (en tu máquina de desarrollo, PowerShell normal)

```powershell
powershell -ExecutionPolicy Bypass -File C:\Users\Carlos\source\repos\NestoAPI\Scripts\Certificado\RenovarCertificadoAeat.ps1
```

El script hace todo solo:
- Encuentra el .pfx más reciente de Descargas (te lo confirma) y te pide su contraseña una vez.
- Comprueba que lleva la clave privada y que es el de la empresa.
- Lo instala en **RDS2016** (producción) por remoto: importación no exportable en el
  almacén de Windows, detecta el app pool del API en IIS y le da permiso de lectura,
  enseña la lista de certificados y limpia los temporales.
- Te ofrece borrar el .pfx al terminar.

No hay que redesplegar, ni reciclar IIS, ni tocar `secretos.config`, ni pegar nada en el
repo: NestoAPI mira el almacén en cada llamada y usa automáticamente el certificado
vigente con la caducidad más lejana (`ProveedorCertificadoAeat`).

### 3. Verificar

Desde Nesto, abrir una ficha de cliente y comprobar un NIF. Si algo falla, en ELMAH sale
"CertificadoAeat: ..." con el motivo.

## Variantes

- **Importarlo también en la máquina de desarrollo** (solo hace falta para depurar VNifV2
  en local): añadir `-TambienEnLocal` al comando (saltará el aviso de elevación de Windows).
- **Si el remoto a RDS2016 fallara** (WinRM apagado): el propio script imprime el plan B —
  copiar el .pfx al servidor y ejecutar allí, como administrador,
  `Scripts\Certificado\ImportarCertificadoAeat.ps1 -RutaPfx <pfx> -AppPool <pool>`.

## Dónde vive cada cosa

| Qué | Dónde |
|---|---|
| Certificado (clave privada, no exportable) | Almacén de Windows `LocalMachine\My` del servidor |
| Código que lo elige | `NestoAPI/Infraestructure/Clientes/ProveedorCertificadoAeat.cs` |
| Aviso de caducidad | ELMAH, diario, desde 15 días antes |
| Script todo-en-uno | `Scripts/Certificado/RenovarCertificadoAeat.ps1` |
| Script de bajo nivel (plan B, en el servidor) | `Scripts/Certificado/ImportarCertificadoAeat.ps1` |
| Fallback legado (a extinguir, issue #388) | `NestoAPI/Infraestructure/Certificados/cert_cam_nv.pfx` + clave `CertificadoDigital` en `secretos.config` |

## Problemas típicos

- **"Este .pfx NO lleva la clave privada"**: al exportar no se marcó "exportar la clave
  privada" → repetir el paso 1.2.
- **"No hay ningún certificado de la AEAT vigente"** (en ELMAH): no hay certificado
  vigente en el almacén del servidor ni fallback → ejecutar el paso 2.
- **La AEAT rechaza la conexión TLS**: el app pool no puede leer la clave privada →
  repetir el paso 2 (o el plan B con `-AppPool`).
- **Importado pero NestoAPI no lo usa**: comprobar que el Subject contiene
  `VATES-A78368255` o `R: A78368255` (si la FNMT cambiara el formato, ajustar
  `ProveedorCertificadoAeat.EsDeLaEmpresa`, que tiene tests).
