/*
    NestoAPI#875 (Novedades 1193 de Reina y 1251; Carlos 17/11/26): el enlace de la conexión online de los eventos (Teams,
    Zoom…) en su propio campo, aparte de «Lugar».

    Eventos.EnlaceOnline nvarchar(500) NULL:
      NULL = sin enlace propio: se sigue leyendo la dirección web que haya en «Lugar», como hasta ahora.
      Una dirección web (http:// o https://) = el enlace que reciben las inscritas online (en los mixtos, solo las que vienen
      online) en el recordatorio de 2 días antes, al inscribirse tarde y, si se cambia, en un correo «Nuevo enlace».
    Solo lo ven y lo cambian Tiendas, Administración, Dirección e Informática (PermisosEventos.PuedeVerEnlaceOnline); el GET de
    eventos no se lo devuelve a nadie más (ni a la tienda online ni a la app de clientes).

    Fuera del EDMX: la API lo lee y lo escribe por SQL y comprueba antes que existe (COL_LENGTH). Sin el script, todo como hoy
    (el enlace sale de «Lugar») y guardar un enlace contesta que falta este script.

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1) La columna, en su propio lote (si ya existe, no se toca)
IF COL_LENGTH('dbo.Eventos', 'EnlaceOnline') IS NULL
    ALTER TABLE dbo.Eventos ADD EnlaceOnline nvarchar(500) NULL;
GO

-- La API lee y actualiza la columna por SQL (SELECT/UPDATE sobre dbo.Eventos); Eventos ya tenía permisos (#611), se repite por si acaso
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT COL_LENGTH('dbo.Eventos', 'EnlaceOnline') AS ColumnaEnlaceOnline;
GO

-- Los eventos online o mixtos que hoy tienen el enlace en «Lugar» siguen funcionando igual. Para pasarlo a su campo, desde Nesto
-- (Clientes › Eventos, «Enlace de la conexión online»). No hace falta tocarlo aquí.
