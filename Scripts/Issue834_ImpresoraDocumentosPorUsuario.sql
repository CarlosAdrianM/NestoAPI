/*
    NestoAPI#834 (06/11/26) - Impresora de documentos POR USUARIO (parámetro ImpresoraDocumentos de NestoAPI#806).
    PROPUESTA PARA QUE LA REVISE CARLOS ANTES DE LANZARLA (sa, BD NV). Las impresoras son las instaladas en RDS2016 el 06/11/26
    (Get-Printer): el nombre tiene que coincidir EXACTAMENTE con el de Windows en el servidor.

    ORDEN DE DESPLIEGUE: este script ANTES de publicar la API con el commit de #834 (sin ImprimeParaEsteUsuario). Si se publica la
    API primero, los usuarios sin fila propia imprimen por «(defecto)» = la HP de Algete, y Reina/Alcobendas/Administración
    verían sus facturas salir en Algete.

    Hoy solo hay dos filas: (defecto) y Alfredo, las dos con «HP LaserJet 600 M601 M602 M603 PCL6» (Algete).

    Propuesta por usuario (quien factura en los últimos 30 días; el resto hereda el (defecto) de Algete):
      Algete         Andre, Santiago, Pedro                → HP LaserJet 600 M601 M602 M603 PCL6   (ya es el (defecto); filas explícitas por claridad)
      Reina          Reina, reina\Laura (ver nota)         → HP LaserJet M602 REINA
      Reina (Pilar)  Pilar                                 → HP Laserjet M700 REINA PILAR   (alternativa: «Pilar Reina HP X580»)
      Alcobendas     Paloma                                → Alcobendas - HP Despacho       (alternativa: «Alcobendas - HP Entrada»)
      Administración Enrique, Aida, Laura, Carlos          → HP LaserJet 700 color MFP M775 PCL 6 ALGETE   (¿o «HP M775»? REVISAR)

    NOTA: el usuario de ParametrosUsuario es el nombre sin dominio tal como lo guarda Nesto («Alfredo», «Reina»). Para la cuenta
    «NUEVAVISION\reina\Laura» (así factura) revisar qué Usuario graba Nesto en ParametrosUsuario antes de dar la fila por buena.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Impresoras TABLE (Usuario varchar(50) NOT NULL, Impresora nvarchar(100) NOT NULL);
INSERT INTO @Impresoras (Usuario, Impresora) VALUES
    ('Andre',    N'HP LaserJet 600 M601 M602 M603 PCL6'),
    ('Santiago', N'HP LaserJet 600 M601 M602 M603 PCL6'),
    ('Pedro',    N'HP LaserJet 600 M601 M602 M603 PCL6'),
    ('Reina',    N'HP LaserJet M602 REINA'),
    ('Pilar',    N'HP Laserjet M700 REINA PILAR'),
    ('Paloma',   N'Alcobendas - HP Despacho'),
    ('Enrique',  N'HP LaserJet 700 color MFP M775 PCL 6 ALGETE'),
    ('Aida',     N'HP LaserJet 700 color MFP M775 PCL 6 ALGETE'),
    ('Laura',    N'HP LaserJet 700 color MFP M775 PCL 6 ALGETE'),
    ('Carlos',   N'HP LaserJet 700 color MFP M775 PCL 6 ALGETE');

BEGIN TRAN;

-- Actualiza las filas que ya existan y crea las que falten (empresa 1, como el (defecto) de Issue806).
UPDATE p SET p.Valor = i.Impresora, p.Usuario = p.Usuario
FROM ParametrosUsuario p JOIN @Impresoras i ON p.Empresa = '1' AND p.Clave = 'ImpresoraDocumentos' AND RTRIM(p.Usuario) = i.Usuario;

INSERT INTO ParametrosUsuario (Empresa, Usuario, Clave, Valor)
SELECT '1', i.Usuario, 'ImpresoraDocumentos', i.Impresora
FROM @Impresoras i
WHERE NOT EXISTS (SELECT 1 FROM ParametrosUsuario p WHERE p.Empresa = '1' AND p.Clave = 'ImpresoraDocumentos' AND RTRIM(p.Usuario) = i.Usuario);

SELECT Empresa, RTRIM(Usuario) Usuario, RTRIM(Valor) Impresora FROM ParametrosUsuario WHERE Clave = 'ImpresoraDocumentos' ORDER BY Usuario;

COMMIT;
