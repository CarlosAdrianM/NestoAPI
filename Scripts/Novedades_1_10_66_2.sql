/*
    Novedades de Nesto 1.10.66.2 (23/11/2026, revisión): cupón de un curso usado antes del día del curso, por excepción (NestoAPI#885, Nesto#648).
    Orden: API → Nesto (misma versión 1.10.66, sube solo la última cifra) → este script. Sin scripts previos.
    Después de publicar la API: dar el permiso «Cupones.PuedeAdelantar» a Manuel («1») y a Sancho («Equipo») en ParametrosUsuario.
    Se omite a propósito: el correo mensual de altas y bajas de stock de las tiendas (NestoAPI#886: no se ve en Nesto; el primer envío es el 01/12)
    y el aviso de «no asistió» de un cupón adelantado (corte 2, sin hacer).
    Ejecutar contra NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @version VARCHAR(23) = '1.10.66.2';
DECLARE @fecha DATE = '2026-11-23';

INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, 'Nuevo', N'Cupón de un curso: usarlo antes del día del curso, por excepción',
       N'El cupón de una inscripción a un curso vale desde el día del curso. Si un cliente necesita descontarlo antes, quien tenga permiso (Dirección, Manuel y, en los clientes de su equipo, Alberto) verá en el pedido o en la plantilla de venta el botón «Usar ya (excepción)» junto al aviso del cupón. Hay que escribir el motivo; después el cupón se añade al pedido con el botón de siempre. Queda apuntado quién lo adelantó y por qué, y se avisa a Administración. Si no ve el botón, pídaselo a quien pueda autorizarlo.',
       'Nesto', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Titulo = N'Cupón de un curso: usarlo antes del día del curso, por excepción');
SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;
