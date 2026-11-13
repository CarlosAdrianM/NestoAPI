/*
    13/11/26 (Carlos): el pago a plazos de un pedido ya creado (NestoAPI#864, Nesto#640, TiendasNuevaVision#99) sale ACTIVADO
    para todos los clientes; los compañeros informan de los errores en producción.

    Lanzar DESPUÉS de publicar la API y Nesto 1.10.61.2 (antes no hace daño: sin Nesto nuevo no hay botón).
    Requiere Issue864_PagoAPlazos.sql (ya lanzado). Ejecutar contra NV como sa. Idempotente.

    Para APAGARLO al momento (el botón deja de verse en Nesto y la app no ofrece nada; las solicitudes vivas siguen su curso):
      UPDATE dbo.ParametrosUsuario SET Valor = 'Apagado', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
      WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos';
*/
SET NOCOUNT ON;
USE NV;

UPDATE dbo.ParametrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos' AND Valor <> 'Activo';
SELECT @@ROWCOUNT AS InterruptorCambiadoAhora;

SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParametrosUsuario WHERE Usuario = '(defecto)' AND Clave LIKE 'PagoAPlazos%';
