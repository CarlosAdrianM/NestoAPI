# Renovar el certificado digital de la AEAT (VNifV2)

**Para qué sirve**: NestoAPI se autentica en el servicio VNifV2 de la AEAT (validación de
NIF contra el censo: facturación, circuito #327, autocurado #383) con el certificado FNMT
de **representante de persona jurídica** (Carlos / Nueva Visión, A78368255). Caduca cada
**2 años**. Cuando falten menos de 15 días, ELMAH avisa a diario ("CertificadoAeat: el
certificado de la AEAT caduca el...").

## El proceso completo (⏱ ~10 minutos + la espera de la FNMT)

### 1. Conseguir el certificado nuevo (esto es lo que tarda días)

1. Solicitar la renovación en la FNMT (https://www.sede.fnmt.gob.es/certificados/certificado-de-representante).
2. Pagar y acreditar la representación (el certificado del Registro Mercantil lo pide la
   propia FNMT; entre ambos suelen tardar ~2 días).
3. Descargar el certificado en el navegador donde se solicitó y **exportarlo a un .pfx CON
   clave privada** (certmgr.msc → Personal → clic derecho → Exportar → "Sí, exportar la
   clave privada" → poner contraseña).

### 2. Instalarlo en el SERVIDOR (RDS2016) — esto es TODO lo que necesita NestoAPI

1. Copiar el .pfx a una carpeta temporal del servidor (p. ej. `C:\Temp\`).
2. Abrir PowerShell **como administrador** en el servidor y ejecutar:

   ```powershell
   C:\ruta\al\repo\Scripts\Certificado\ImportarCertificadoAeat.ps1 -RutaPfx C:\Temp\certificado_nuevo.pfx -AppPool Api
   ```

   (Si no recuerdas el nombre del app pool: `Get-Website | Select Name, ApplicationPool`.
   Si el script no está a mano, está en el repo en `Scripts/Certificado/`.)

3. Comprobar en la salida del script que el certificado nuevo aparece el PRIMERO en la
   tabla (caducidad más lejana) y con `ClavePrivada = True`.
4. **Borrar el .pfx** de `C:\Temp\` (y de descargas/correo si quedó por ahí).

**No hay que hacer nada más**: ni redesplegar, ni reciclar IIS, ni tocar `secretos.config`,
ni pegar el .pfx en el repo. NestoAPI busca en el almacén de Windows en cada llamada y usa
automáticamente el certificado vigente con la caducidad más lejana (`ProveedorCertificadoAeat`).

### 3. Instalarlo en la máquina de DESARROLLO (opcional, para depurar VNifV2 en local)

El mismo script, sin `-AppPool` (en dev no hay IIS; el usuario administrador ya puede leer
la clave):

```powershell
.\ImportarCertificadoAeat.ps1 -RutaPfx C:\Temp\certificado_nuevo.pfx
```

### 4. Verificar que funciona

Desde Nesto, abrir una ficha de cliente y comprobar un NIF (o esperar a la siguiente
facturación). Si algo falla, en ELMAH saldría "CertificadoAeat: ..." con el motivo.

## Dónde vive cada cosa

| Qué | Dónde |
|---|---|
| Certificado (clave privada, no exportable) | Almacén de Windows `LocalMachine\My` del servidor (y de dev) |
| Código que lo elige | `NestoAPI/Infraestructure/Clientes/ProveedorCertificadoAeat.cs` |
| Aviso de caducidad | ELMAH, diario, desde 15 días antes |
| Fallback legado (a extinguir, issue #388) | `NestoAPI/Infraestructure/Certificados/cert_cam_nv.pfx` + clave `CertificadoDigital` en `secretos.config` |

## Problemas típicos

- **"No hay ningún certificado de la AEAT vigente"**: no hay certificado en el almacén (o
  está caducado) y el fallback .pfx ya no existe/caducó → ejecutar el paso 2.
- **La AEAT rechaza la conexión TLS**: el app pool no puede leer la clave privada →
  repetir el script con `-AppPool` correcto (o revisar la ACL del fichero de clave en
  `C:\ProgramData\Microsoft\Crypto\...`).
- **Importado pero NestoAPI no lo usa**: comprobar que el Subject contiene
  `VATES-A78368255` o `R: A78368255` (si la FNMT cambiara el formato, ajustar
  `ProveedorCertificadoAeat.EsDeLaEmpresa`, que tiene tests).
