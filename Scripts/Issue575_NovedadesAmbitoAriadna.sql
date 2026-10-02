-- NestoAPI#575 (fase 2): Novedades de Ariadna (la app del almacén).
-- Amplía CK_Novedades_Ambito para admitir 'Ariadna'. Sin esto, una sugerencia o un «Algo no funciona»
-- enviado desde Ariadna falla al grabar (la API ya lo graba con Ambito = 'Ariadna').
-- Lanzar como sa en NV ANTES de publicar la API que lo incluye. Se puede repetir sin miedo.
-- No hace falta GRANT: no hay tablas nuevas.
USE NV;
GO

-- Antes: qué hay
SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_Novedades_Ambito';

BEGIN TRAN;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Novedades_Ambito')
    ALTER TABLE dbo.Novedades DROP CONSTRAINT CK_Novedades_Ambito;

ALTER TABLE dbo.Novedades WITH CHECK ADD CONSTRAINT CK_Novedades_Ambito
    CHECK ([Ambito] = N'NestoApp' OR [Ambito] = N'NestoAPI' OR [Ambito] = N'Nesto' OR [Ambito] = N'Ariadna');

COMMIT;

-- Después: debe incluir N'Ariadna'
SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_Novedades_Ambito';
