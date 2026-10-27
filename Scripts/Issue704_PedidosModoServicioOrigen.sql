/*
    NestoAPI#704 (gemela de NestoApp#250; caso del pedido 929152): tabla PedidosModoServicioOrigen.

    ¿El modo de servicio lo eligió el vendedor o venía puesto? El pedido 929152 (cliente 29298/0, David, 22/10/2026, NestoApp) se grabó
    con ModoServicio = 4 («Ahora lo que hay, el resto de una vez») y el vendedor asegura que había elegido «Todo junto». Casi seguro el
    modo era el sugerido y nadie lo tocó, pero hoy no queda rastro. Desde ahora, cada vez que la API crea un pedido (POST api/PedidosVenta;
    también los de TNV y los que importa Nesto) y cada vez que una modificación (PUT) cambia el modo efectivo, deja aquí una fila con:

    Operacion:            Crear o Modificar.
    Origen:               lo que dice el cliente en PedidoVentaDTO.OrigenModoServicio: Usuario (lo eligió el vendedor), Sugerido (la
                          sugerencia de POST api/PedidosVenta/ModoServicioSugerido aplicada sin tocarla), PorDefecto (parámetro o defecto)
                          o Corregido (tras un rechazo de #518 o porque dejó de tener sentido, #187). NULL = cliente antiguo que no lo manda.
    ModoEnviado:          el modoServicio tal como vino en el DTO (NULL = no lo mandó y nació en el sugerido o el defecto).
    ServirJuntoEnviado:   el servirJunto que vino.
    ModoSugeridoCliente:  la sugerencia que el cliente tenía en pantalla al grabar (PedidoVentaDTO.ModoServicioSugerido), si la manda.
    ModoGuardado:         el modo efectivo con el que ha quedado el pedido (CabPedidoVta.ModoServicio).
    ModoAnterior:         al modificar, el que tenía antes. Al crear, NULL.
    ModoSugeridoServidor: lo que sugería el servidor con el stock de ese momento (SugeridorModoServicio, ya con el parámetro forzado del
                          usuario). NULL si no se calculó (modificaciones) o falló.
    PermitidosServidor:   los modos permitidos del servidor en ese momento, «1,2,4».
    MotivoServidor:       el motivo de la sugerencia del servidor.
    Aplicacion:           la cabecera X-Aplicacion (Nesto, NestoApp, TiendasNuevaVision) o lo que se infiere del usuario.

    Lectura típica (otro «yo puse todo junto»):
      SELECT * FROM dbo.PedidosModoServicioOrigen WHERE Pedido = 929152 ORDER BY Fecha;
      -> Origen = 'Sugerido' y ModoEnviado = ModoSugeridoServidor = ModoGuardado: el vendedor no lo tocó.
      -> Origen = 'Usuario' y ModoEnviado <> ModoGuardado: la API lo corrigió (solo en tienda, #518).

    No está en el EDMX (la API escribe con SQL a mano, como PedidosOrigenPush #709 y PedidosAnomalias #715): se puede lanzar antes o
    después del deploy. Sin ella la API no guarda nada (un aviso a ELMAH por proceso) y los pedidos se graban igual.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT e INSERT. Sin UPDATE ni DELETE: las filas no se tocan.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosModoServicioOrigen', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosModoServicioOrigen (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_PedidosModoServicioOrigen_Fecha DEFAULT (GETDATE()),
        Operacion varchar(10) NOT NULL,          -- Crear, Modificar
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Cliente char(10) NULL,
        Contacto char(3) NULL,
        Usuario varchar(50) NULL,
        Aplicacion varchar(20) NULL,             -- Nesto, NestoApp, TiendasNuevaVision, Desconocida
        Origen varchar(20) NULL,                 -- Usuario, Sugerido, PorDefecto, Corregido; NULL = cliente antiguo
        ModoEnviado tinyint NULL,                -- el modoServicio del DTO; NULL = no lo mandó
        ServirJuntoEnviado bit NOT NULL CONSTRAINT DF_PedidosModoServicioOrigen_ServirJuntoEnviado DEFAULT (0),
        ModoSugeridoCliente tinyint NULL,        -- PedidoVentaDTO.ModoServicioSugerido
        ModoGuardado tinyint NOT NULL,           -- el efectivo con el que ha quedado
        ModoAnterior tinyint NULL,               -- solo al modificar
        ModoSugeridoServidor tinyint NULL,       -- la sugerencia del servidor al grabar
        PermitidosServidor varchar(10) NULL,     -- «1,2,4»
        MotivoServidor varchar(400) NULL,

        CONSTRAINT PK_PedidosModoServicioOrigen PRIMARY KEY CLUSTERED (Id)
    );
END
GO

-- Para buscar por pedido (lo normal: «qué pasó con el 929152»). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PedidosModoServicioOrigen_Pedido' AND object_id = OBJECT_ID('dbo.PedidosModoServicioOrigen'))
    CREATE INDEX IX_PedidosModoServicioOrigen_Pedido ON dbo.PedidosModoServicioOrigen (Empresa, Pedido) INCLUDE (Fecha, Operacion, Origen, ModoEnviado, ModoGuardado);
GO

GRANT SELECT, INSERT ON dbo.PedidosModoServicioOrigen TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.PedidosModoServicioOrigen', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.PedidosModoServicioOrigen) AS Filas;
GO

-- Lo de la última semana, con cuántos pedidos nacieron sin que el vendedor tocara el modo:
-- SELECT o.Fecha, o.Pedido, o.Aplicacion, o.Usuario, o.Operacion, o.Origen, o.ModoEnviado, o.ModoSugeridoCliente, o.ModoSugeridoServidor,
--        o.ModoGuardado, o.ModoAnterior, o.PermitidosServidor
-- FROM dbo.PedidosModoServicioOrigen o WITH (NOLOCK)
-- WHERE o.Fecha >= DATEADD(day, -7, GETDATE())
-- ORDER BY o.Fecha DESC;
