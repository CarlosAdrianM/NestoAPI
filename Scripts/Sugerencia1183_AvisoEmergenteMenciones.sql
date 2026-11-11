-- Sugerencia 1183 (Laura Magán, Administración, 11/11/26): «no me entero cuando me mencionan en un aviso de la campanita».
-- Parámetro de usuario nuevo AvisoEmergenteMenciones ("1" = cuando le mencionan en Novedades, Nesto enseña una ventanita
-- en la esquina de la pantalla, sin sonido, que se cierra sola; cualquier otro valor = no). El propio usuario lo enciende y
-- lo apaga desde el panel de la campana («Avisarme con una ventana cuando me mencionen»).
-- Fila «(defecto)» = '0' (apagado). Sin ella Nesto no falla (sale apagado), pero la API no puede crear la fila de cada
-- usuario al leerla y el interruptor no podría guardar. Laura Magán (Magan) lo tiene encendido.
-- Sin GRANT: ParametrosUsuario ya lo lee y escribe NestoAPI. Idempotente: se puede repetir sin miedo.
-- Lanzar en la BD NV (NestoConnection).

-- Antes
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'AvisoEmergenteMenciones';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoEmergenteMenciones')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AvisoEmergenteMenciones', '(defecto)', '0', 'Sugerencia1183', GETDATE());
END

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = 'Magan' AND Clave = 'AvisoEmergenteMenciones')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AvisoEmergenteMenciones', 'Magan', '1', 'Sugerencia1183', GETDATE());
END
GO

-- Después
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'AvisoEmergenteMenciones';
