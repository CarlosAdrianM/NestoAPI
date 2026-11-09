-- Sugerencia 1141 (Paloma, 09/11/26): en Clientes › Eventos, «Ver anuladas» en los inscritos, desmarcado por defecto y
-- recordado por usuario. Parámetro de usuario nuevo EventosVerAnuladas ("1" = se ven las anuladas; cualquier otro valor = no).
-- Fila «(defecto)» = '0' (no se ven). Sin esta fila Nesto no falla (sale desmarcado), pero la API no puede crear la fila
-- de cada usuario al leerla.
-- Sin GRANT: ParametrosUsuario ya lo lee y escribe NestoAPI. Idempotente: se puede repetir sin miedo.
-- Lanzar en la BD NV (NestoConnection).

-- Antes
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'EventosVerAnuladas';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'EventosVerAnuladas')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EventosVerAnuladas', '(defecto)', '0', 'Sugerencia1141', GETDATE());
END
GO

-- Después
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'EventosVerAnuladas';
