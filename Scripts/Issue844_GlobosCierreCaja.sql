/*
    NestoAPI#844 (09/11/26): globos de Ariadna para que el repartidor cierre la caja al terminar (Miguel no lo había hecho nunca:
    Administración hacía los traspasos a mano). Desde esta versión la API no deja terminar la jornada con dinero en la caja ni
    empezar el día siguiente con dinero de días anteriores.
    Anclas nuevas en Ariadna 2.32: «Reparto.CerrarCaja» (la acción «Cerrar caja» del menú «⋯» y F8 en «Mis cobros»),
    «Reparto.TerminarJornada» (el botón de la furgoneta) y «CierreCaja.Importe» (el cuadro del importe). Con el menú cerrado o la
    furgoneta sin abrir, el globo sale como tarjeta centrada (lo normal).
    VersionDesde = '2.32': la versión siguiente a la 2.31; si al publicar sale otra, cambiarla aquí.
    Ejecutar en SSMS contra NV (sa o nuevavision). Idempotente (por título).
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @insertados int = 0;

IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Titulo = N'Cierre la caja al terminar el reparto')
BEGIN
    INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
    VALUES ('Ariadna', '2.32', 'Reparto', 'Reparto.CerrarCaja', N'Cierre la caja al terminar el reparto',
            N'Al terminar el reparto, cuente el dinero que trae y ciérrelo aquí (menú «⋯» → Cerrar caja, o F8 en «Mis cobros»): pasa a la caja pendiente de recibir y Administración lo recoge.',
            NULL, @hoy, DATEADD(day, 45, @hoy), 17);
    SET @insertados += @@ROWCOUNT;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Titulo = N'Para terminar la jornada, la caja cerrada')
BEGIN
    INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
    VALUES ('Ariadna', '2.32', 'Reparto', 'Reparto.TerminarJornada', N'Para terminar la jornada, la caja cerrada',
            N'Si trae dinero en la caja, al terminar la jornada Ariadna le lleva a «Cerrar caja». Y si un día se queda sin cerrar, a la mañana siguiente no podrá cargar, recoger ni cobrar hasta cerrarla.',
            NULL, @hoy, DATEADD(day, 45, @hoy), 18);
    SET @insertados += @@ROWCOUNT;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Titulo = N'Teclee lo que ha contado')
BEGIN
    INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
    VALUES ('Ariadna', '2.32', 'CierreCaja', 'CierreCaja.Importe', N'Teclee lo que ha contado',
            N'Cuente los billetes y las monedas que trae y teclee lo que ha contado. Si no cuadra con la caja, no se cierra y se avisa a Administración para que lo revise con usted.',
            NULL, @hoy, DATEADD(day, 45, @hoy), 1);
    SET @insertados += @@ROWCOUNT;
END

SELECT @insertados AS GlobosInsertadosAhora;
SELECT Id, VersionDesde, Pantalla, Ancla, Titulo, FechaDesde, FechaHasta, Orden
FROM dbo.Globos
WHERE Aplicacion = 'Ariadna' AND Ancla IN ('Reparto.CerrarCaja', 'Reparto.TerminarJornada', 'CierreCaja.Importe');
