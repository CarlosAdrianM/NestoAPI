-- Carlos (04/11/26): encolar en Nesto_sync las 99 referencias nulas en Nesto que siguen activas en la web (16 con stock en la
-- tienda, 15 sin stock pero con ventas en el último año y 68 sin stock ni ventas) para que nesto_sync las desactive en PrestaShop.
-- Ejecutar en SSMS contra NV (vale el login nuevavision: tiene INSERT en Nesto_sync). Idempotente: no repite las ya pendientes.
-- OJO (avisos del análisis): las 16 con stock desaparecen de la web con unidades dentro; si alguna está nula en Nesto por error,
-- corregirla en Nesto ANTES de encolar (el módulo nunca reactiva por su cuenta). El Cofre 24 H y el tinte de pestañas nº 1
-- (17 y 13 ventas este año): si están descatalogados de verdad, Laura pone la redirección al sustituto en el back office.
SET NOCOUNT ON;
DECLARE @refs TABLE (Ref varchar(15));
INSERT INTO @refs VALUES
-- Con stock en la tienda (16)
('42908'),('43342'),('42907'),('43340'),('38697'),('43166'),('44061'),('44419'),('44958'),('44732'),('45028'),('41924'),('42602'),('44158'),('41492'),('44477'),
-- Sin stock, con ventas en el último año (15)
('44967'),('39796'),('45231'),('43142'),('22599'),('45213'),('40586'),('41906'),('45270'),('15181'),('37549'),('39096'),('43407'),('43708'),('45227'),
-- Sin stock ni ventas (68)
('25377'),('27246'),('32867'),('32913'),('33398'),('33487'),('33647'),('33809'),('33810'),('33871'),('33946'),('33982'),('34680'),('34684'),('35001'),('35052'),('35132'),('35471'),('35574'),('35734'),('35784'),('35883'),('35887'),('35888'),('36233'),('36251'),('36325'),('36481'),('36542'),('36561'),('36666'),('36726'),('36758'),('37292'),('37305'),('37760'),('37966'),('38932'),('39489'),('39490'),('39491'),('40228'),('40301'),('40500'),('40660'),('40716'),('40786'),('40818'),('40870'),('40871'),('41338'),('41550'),('41593'),('41724'),('41759'),('43411'),('43689'),('44127'),('44150'),('44154'),('44155'),('44156'),('44295'),('44332'),('44360'),('44663'),('44887'),('44900');

-- A) Antes: cuántas existen, su estado en Nesto y cuántas ya estaban pendientes
;WITH Situacion AS (
    SELECT r.Ref,
           CASE WHEN p.Número IS NOT NULL THEN 1 ELSE 0 END Existe,
           CASE WHEN p.Estado < 0 THEN 1 ELSE 0 END Nulo,
           CASE WHEN p.Estado >= 0 THEN 1 ELSE 0 END Activo,
           CASE WHEN pend.Ref IS NOT NULL THEN 1 ELSE 0 END YaPendiente
    FROM @refs r
    LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = '1' AND RTRIM(p.Número) = r.Ref
    LEFT JOIN (SELECT DISTINCT RTRIM(ModificadoId) Ref FROM Nesto_sync WITH (NOLOCK) WHERE Tabla = 'Productos' AND Sincronizado IS NULL) pend ON pend.Ref = r.Ref
)
SELECT COUNT(*) Lista, SUM(Existe) Existen, SUM(Nulo) NulosEnNesto, SUM(Activo) ActivosEnNesto, SUM(YaPendiente) YaPendientes
FROM Situacion;

-- B) Las que NO existen o NO están nulas en Nesto (revisar antes de seguir: encolarlas solo las vuelve a publicar tal como están)
SELECT r.Ref, p.Estado, LEFT(p.Nombre, 40) Nombre
FROM @refs r LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = '1' AND RTRIM(p.Número) = r.Ref
WHERE p.Número IS NULL OR p.Estado >= 0;

-- C) Encolar (solo las que existen y no están ya pendientes)
INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Productos', r.Ref, 'Encolado 04/11 nulos web (Carlos)', GETDATE()
FROM @refs r
WHERE EXISTS (SELECT 1 FROM Productos p WITH (NOLOCK) WHERE p.Empresa = '1' AND RTRIM(p.Número) = r.Ref)
  AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns WITH (NOLOCK) WHERE ns.Tabla = 'Productos' AND RTRIM(ns.ModificadoId) = r.Ref AND ns.Sincronizado IS NULL);
SELECT @@ROWCOUNT AS Encoladas;

-- D) Después (y para ver cómo avanza): pendientes por tabla
SELECT Tabla, COUNT(*) Pendientes FROM Nesto_sync WITH (NOLOCK) WHERE Sincronizado IS NULL GROUP BY Tabla;
