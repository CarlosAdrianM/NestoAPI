-- NestoAPI#532 (corte 1): interruptor del recordatorio de reposición (ventas de Nesto, todos los canales).
-- El job 'recordatorio-reposicion-semanal' de Hangfire corre los jueves a las 5:30, pero NO HACE NADA
-- mientras no exista esta fila (sin fila = APAGADO, que es como nace al publicar).
--
-- Este script lo pone en modo SOMBRA: cada jueves manda UN correo a Carlos (de momento solo a él)
-- con los clientes y productos a los que se les recordaría reponer, el grupo de
-- control, los que se quedan fuera y por qué, y una muestra del correo. NO escribe a ningún cliente ni
-- registra nada (eso es el corte 2).
-- Se lee en cada ejecución: el cambio se nota en la siguiente pasada, sin publicar ni reiniciar.
-- Ejecutar en NV (NestoConnection). Idempotente.
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicion', 'Sombra', 'NestoAPI#532', GETDATE());
ELSE
    UPDATE dbo.ParámetrosUsuario SET Valor = 'Sombra', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion';

-- Comprobación
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'RecordatorioReposicion%';
GO

-- APAGAR (vuelve a no hacer nada):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0', Usuario2 = 'NestoAPI#532', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicion';

-- OPCIONAL: cambiar la lista blanca de consumibles (sin fila = la de por defecto, ConsumiblesReposicion.POR_DEFECTO).
-- «COS» = todo el grupo, «PEL/TIN» = un subgrupo, «-COS/MMP» = quitar un subgrupo de un grupo entero.
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionConsumibles')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'RecordatorioReposicionConsumibles',
--             'COS, -COS/MMP, -COS/PRG, -COS/PRO, ACC/002, ACC/005, ACC/006, PEL/ACB, PEL/DES, PEL/LAV, PEL/MYD, PEL/PEL, PEL/TIN, PEL/TRA, PEL/UTJ',
--             'NestoAPI#532', GETDATE());

-- PROBAR SIN ESPERAR AL JUEVES (no hace falta encender nada; solo escribe al equipo interno):
--   GET  api/CorreosPostCompra/Reposicion                -> la lista en JSON (cálculo en seco)
--   POST api/CorreosPostCompra/Reposicion/EnviarSombra   -> manda ahora el correo sombra
-- Ambos con token (Authorize). ?fecha=yyyy-MM-dd para ver otro día.
-- O en el panel de Hangfire: Recurring jobs -> 'recordatorio-reposicion-semanal' -> Trigger now (con la fila en Sombra).
