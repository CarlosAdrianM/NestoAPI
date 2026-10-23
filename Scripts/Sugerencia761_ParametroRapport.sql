-- Sugerencia 761 (Paloma, 23/10/26): la cinta «Puede ofrecer» del rapport recuerda si va plegada aparte de la de la
-- plantilla. Parámetro de usuario nuevo SugerenciasRapportPlegadas ("1" = plegada; cualquier otro valor = desplegada).
-- Fila «(defecto)» con el mismo valor que hoy tiene SugerenciasPlantillaPlegadas en la suya ('0' el 23/10/26: desplegada).
-- Sin esta fila Nesto no falla (la cinta sale desplegada), pero la API no puede crear la fila de cada usuario al leerla.
-- Sin GRANT: ParametrosUsuario ya lo lee y escribe NestoAPI. Idempotente: se puede repetir sin miedo.
-- Lanzar en la BD NV (NestoConnection).

-- Antes
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave IN ('SugerenciasPlantillaPlegadas', 'SugerenciasRapportPlegadas');

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SugerenciasRapportPlegadas')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'SugerenciasRapportPlegadas', '(defecto)', '0', 'Sugerencia761', GETDATE());
END
GO

-- Después
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave IN ('SugerenciasPlantillaPlegadas', 'SugerenciasRapportPlegadas');
