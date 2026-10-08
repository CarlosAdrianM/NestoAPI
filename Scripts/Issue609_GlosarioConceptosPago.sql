-- NestoAPI#609: glosario fijo de nombres propios para revisar el concepto de los enlaces de pago
-- (POST api/Pagos/RevisarConcepto). La API lee TODAS las filas de «(defecto)» cuya Clave empiece por
-- 'GlosarioConceptosPago' (en orden de clave) y separa los términos por comas o punto y coma.
-- Valor es char(162): cuando una fila no dé para más, se añade GlosarioConceptosPago3, 4…
-- Editable sin publicar (la API lo guarda en caché 10 minutos).
--
-- ORDEN: indiferente (sin las filas, la revisión sigue con productos y eventos). BD: NV (NestoConnection).
-- Sin GRANTs (INSERT de datos). Lanzar como sa.

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'GlosarioConceptosPago' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'GlosarioConceptosPago', '(defecto)',
        N'Masterclass, Microneedling, Cloasma, Léntigos, PDRN, NAD, Exosomas, Hirsutismo, Maderoterapia, Lifting coreano, Nueva Visión',
        'NestoAPI', GETDATE());
END
GO

-- Los que aparecen bien escritos en PagosTPV.Descripcion desde agosto de 2026.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'GlosarioConceptosPago2' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'GlosarioConceptosPago2', '(defecto)',
        N'Cloasma gravídico, Léntigos solares, Lifting de pestañas, Acné, Depilación profesional, Agenda proactiva, Formación',
        'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN (2 filas, con tildes bien):
SELECT Empresa, Clave, Usuario, RTRIM(Valor) AS Valor, LEN(Valor) AS Longitud FROM ParametrosUsuario
WHERE Clave LIKE 'GlosarioConceptosPago%' AND Usuario = '(defecto)'
ORDER BY Clave;
GO
