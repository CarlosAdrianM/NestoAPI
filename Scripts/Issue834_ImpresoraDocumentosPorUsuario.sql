/*
    NestoAPI#834 (06/11/26) - Impresora de documentos POR USUARIO (parámetro ImpresoraDocumentos de NestoAPI#806).
    Decisión de Carlos (06/11/26 14:1x): Nesto sigue imprimiendo en LOCAL con la impresora por defecto de cada puesto; el servidor
    imprime solo para los usuarios SIN dominio (Ariadna), que no tienen impresora propia. Por eso solo hacen falta filas para los
    usuarios de Ariadna de las tiendas (Algete ya es el (defecto), «HP LaserJet 600 M601 M602 M603 PCL6»).

    Los nombres son los de las impresoras instaladas en RDS2016 (Get-Printer, 06/11/26) y tienen que coincidir EXACTAMENTE.
    El Usuario de ParametrosUsuario es el nombre de Identity sin dominio («Reina», «Paloma»), que es como entra Ariadna.
    Se puede lanzar antes o después de publicar la API: con la API actual ya se usa (los usuarios de Ariadna imprimen en servidor
    desde el 04/11).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Impresoras TABLE (Usuario varchar(50) NOT NULL, Impresora nvarchar(100) NOT NULL);
INSERT INTO @Impresoras (Usuario, Impresora) VALUES
    ('Reina',  N'HP LaserJet M602 REINA'),       -- usuario de Ariadna de la tienda de Reina
    ('Paloma', N'Alcobendas - HP Entrada');      -- usuario de Ariadna de Alcobendas

BEGIN TRAN;

UPDATE p SET p.Valor = i.Impresora
FROM ParametrosUsuario p JOIN @Impresoras i ON p.Empresa = '1' AND p.Clave = 'ImpresoraDocumentos' AND RTRIM(p.Usuario) = i.Usuario;

INSERT INTO ParametrosUsuario (Empresa, Usuario, Clave, Valor)
SELECT '1', i.Usuario, 'ImpresoraDocumentos', i.Impresora
FROM @Impresoras i
WHERE NOT EXISTS (SELECT 1 FROM ParametrosUsuario p WHERE p.Empresa = '1' AND p.Clave = 'ImpresoraDocumentos' AND RTRIM(p.Usuario) = i.Usuario);

SELECT Empresa, RTRIM(Usuario) Usuario, RTRIM(Valor) Impresora FROM ParametrosUsuario WHERE Clave = 'ImpresoraDocumentos' ORDER BY Usuario;

COMMIT;
