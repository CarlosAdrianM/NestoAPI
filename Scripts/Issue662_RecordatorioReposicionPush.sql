-- NestoAPI#662: push de reposición a los clientes de NestoTiendas (segundo canal del recordatorio de #532).
--
-- 1. Columna RecibirConsejos en DispositivosNotificaciones: si el cliente quiere consejos y recordatorios de reposición
--    (TNV#87, preferencia de la app). Nace a 1 para todos. Los avisos de pedidos, protocolos y cheques NO la miran.
-- 2. Filas «(defecto)» de los dos parámetros nuevos:
--      RecordatorioReposicionPush       = Apagado  (Apagado / Sombra / Activo; sin fila o cualquier otra cosa = apagado)
--      RecordatorioReposicionHoraLimite = 11:00    (HH:mm; desde esa hora ya no se manda ese día)
--    El job 'recordatorio-reposicion-push-diario' corre de lunes a jueves a las 9:30 y no hace nada mientras esté Apagado.
--    El correo semanal de #532 (parámetro RecordatorioReposicion) no cambia.
--
-- ORDEN: lanzar ANTES de publicar el API que trae #662. El API nuevo lee y escribe la columna RecibirConsejos en TODAS
-- las consultas de DispositivosNotificaciones (registro de dispositivos y todas las push): sin la columna, fallarían.
-- Se puede lanzar dos veces sin problema.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER). No hace falta GRANT: la tabla ya la escribe el API.

USE NV;
GO

IF COL_LENGTH('dbo.DispositivosNotificaciones', 'RecibirConsejos') IS NULL
BEGIN
    ALTER TABLE dbo.DispositivosNotificaciones
        ADD RecibirConsejos bit NOT NULL CONSTRAINT DF_DispositivosNotificaciones_RecibirConsejos DEFAULT 1;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPush')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionPush', 'Apagado', 'NestoAPI#662', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionHoraLimite')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'RecordatorioReposicionHoraLimite', '11:00', 'NestoAPI#662', GETDATE());
GO

-- Comprobación
SELECT COL_LENGTH('dbo.DispositivosNotificaciones', 'RecibirConsejos') AS RecibirConsejos,
       (SELECT COUNT(*) FROM dbo.DispositivosNotificaciones WHERE RecibirConsejos = 1) AS DispositivosConConsejos;
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'RecordatorioReposicion%';
GO

-- MODO SOMBRA (cada L-J a las 9:30 llega a Informática a quién le habría llegado la push y con qué texto; no se envía nada):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Sombra', Usuario2 = 'NestoAPI#662', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPush';

-- ENCENDER DE VERDAD (envía la push a los clientes):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#662', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPush';

-- APAGAR:
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Apagado', Usuario2 = 'NestoAPI#662', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionPush';

-- CAMBIAR LA HORA LÍMITE (tras confirmarla con almacén):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '12:00', Usuario2 = 'NestoAPI#662', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'RecordatorioReposicionHoraLimite';

-- Medir el efecto: Scripts/Issue662_MedicionReposicionPush.sql
