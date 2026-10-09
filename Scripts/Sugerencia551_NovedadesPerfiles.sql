/*
    Sugerencia 551 (Alberto Sancho, 09/10/26): «que solo salgan las novedades que le afecten a cada uno».

    Ejecutar en SSMS contra NV (NestoConnection), como sa. Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy de la API: mientras la columna no exista, las novedades salen
    igual, todas para todos (la API lee los perfiles en una consulta aparte y, si falla, no filtra).
    El relleno de abajo solo toca las novedades que siguen sin perfiles (no pisa lo que se haya cambiado).

    QUÉ HACE
      1) Columna Novedades.Perfiles nvarchar(200) NULL: a quién afecta cada novedad, separados por comas.
         Valores: Vendedores, Almacén, Tiendas, Administración (con o sin tilde, sin distinguir mayúsculas).
         NULL, vacía o «Todos» = para todos (así siguen todas las de antes de esta fecha).
      2) Rellena las de Nesto de este mes (1.10.36.0 → 1.10.39.1). Las de Ariadna y NestoApp se quedan para
         todos: cada app ya es de un solo puesto (almacén y vendedores).

    PERFIL DE CADA USUARIO (lo deduce la API del token: ReglasPerfilesNovedades.DeducirPerfiles)
      Dirección, Informatica                     → ven todas
      Almacén                                    → Almacén
      Tiendas                                    → Tiendas
      Administración, Compras, TiendaOnline      → Administración
      Comerciales (o IsVendedor en NestoApp)     → Vendedores, solo si no tiene ninguno de los tres de arriba
      ninguno de ellos (o sin token)             → ven todas
    Cada uno ve por defecto las suyas y las que son para todos; en Nesto, «Ver todas las novedades» enseña el resto.

    LA API
      GET api/Novedades?todas=true            → sin el filtro por perfil (sin él, solo las suyas)
      GET api/Novedades/MisPerfiles           → { Perfiles, VeTodas, PuedeEditar, Disponibles }
      PUT api/Novedades/{id}/Perfiles         → { "Perfiles": ["Almacén", "Tiendas"] }  ([] o ["Todos"] = para todos)
      PUT api/Novedades/Sugerencias/{id}      → acepta también "Perfiles" al implementarla
    Las sugerencias y los avisos de «Algo no funciona» no se filtran nunca.

    CÓMO PONER PERFILES EN LOS SCRIPTS DE NOVEDADES DE CADA VERSIÓN (Novedades_1_10_*.sql)
      Una columna más en la tabla variable y en el INSERT; NULL = para todos:

        DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

        INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
            ('Nuevo',     N'Recibir reposición: ...',           N'...', N'Tiendas'),
            ('Mejorado',  N'La plantilla dice qué día ...',     N'...', N'Vendedores,Tiendas'),
            ('Corregido', N'La ventana de Novedades ya no ...', N'...', NULL);

        INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
        SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
        FROM @novedades n
        WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

      Criterio: el puesto que USA la pantalla que cambia. Si le sirve a cualquiera (la propia ventana de
      Novedades, guardar un pedido...), NULL. Una sugerencia implementada se marca con el PUT de arriba
      añadiendo "Perfiles" junto a "Version".
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'Perfiles') IS NULL
    ALTER TABLE dbo.Novedades ADD Perfiles nvarchar(200) NULL;
GO

-- La API ya tiene SELECT y UPDATE sobre la tabla Novedades (la columna nueva los hereda).

-- Relleno de las novedades de Nesto de octubre/2026 (propuesta para revisar; NULL = para todos)
UPDATE n SET Perfiles = v.Perfiles
FROM dbo.Novedades n
JOIN (VALUES
    -- 1.10.36.0
    (450, N'Administración'),                  -- Cursos de la tienda online exentos de IVA
    (451, N'Almacén'),                         -- El picking no saca solo los regalos
    (452, N'Almacén,Tiendas'),                 -- Al hacer el albarán pregunta por lo de «carpeta»
    (453, N'Vendedores,Administración'),       -- Cambiar el cliente de una nota de entrega
    (454, N'Administración'),                  -- Administración crea albaranes y facturas desde el pedido
    (456, N'Vendedores,Administración'),       -- Al unir pedidos explica por qué no se puede
    (457, N'Vendedores,Administración'),       -- Clientes de fuera de la UE sin avisos falsos de NIF
    (458, N'Almacén,Administración'),          -- Portes y comisión de reembolso con albarán
    (459, N'Vendedores,Administración'),       -- Cuentas bancarias del cliente no se guardan dos veces
    -- 1.10.36.1
    (462, N'Vendedores,Administración'),       -- Pedidos solo de cursos a la serie CV
    (463, N'Almacén'),                         -- Facturar al imprimir la etiqueta
    (464, N'Almacén'),                         -- El picking dice si el cliente cierra el día de la entrega
    (465, N'Almacén'),                         -- Facturar rutas con notas de entrega de ruta propia
    (466, N'Vendedores,Tiendas'),              -- Ofertas combinadas con importe mínimo
    (467, N'Vendedores,Tiendas'),              -- No se sugiere un 6+1 sobre precio rebajado
    (468, N'Administración'),                  -- Pedidos de compra: precio por cantidad del proveedor
    -- 1.10.36.2
    (469, N'Almacén,Administración'),          -- «Crear albarán» con una nota de entrega
    -- 1.10.36.3
    (482, N'Vendedores,Administración'),       -- Clientes con dirección de fuera de España
    (483, N'Vendedores,Administración'),       -- Mandar facturas por correo desde la ficha del cliente
    (485, N'Tiendas'),                         -- Recibir las reposiciones en la tienda
    (486, N'Almacén'),                         -- Etiquetas de hueco del almacén
    (488, N'Tiendas,Administración'),          -- Cobros en caja con un abono
    (489, N'Almacén'),                         -- Agencias: etiqueta de un envío pendiente
    (491, N'Almacén'),                         -- Agencias avisa de lo pagado por adelantado
    -- 1.10.37.0
    (501, N'Administración'),                  -- Pedido de la tienda online con solo un TiCKET
    (505, N'Tiendas'),                         -- Recibir reposición abre sola la pendiente
    (508, N'Vendedores,Administración'),       -- Cambiar el almacén de un pedido entero
    (509, N'Almacén'),                         -- Retorno de un envío de CTT
    (510, N'Tiendas,Administración'),          -- Cajas: copiar el número de documento
    (512, N'Tiendas'),                         -- Mandar la reposición a Algete desde la tienda
    (513, N'Vendedores,Administración'),       -- El código postal vale como lo escribas
    (514, N'Almacén'),                         -- Modificar un envío ya registrado en la agencia
    -- 1.10.38.0
    (520, N'Vendedores'),                      -- Correo diario de rapports
    (521, N'Vendedores,Administración'),       -- Eventos y señales de cursos reembolsables
    (522, N'Vendedores'),                      -- El correo del pedido dice el modo de facturación
    (523, N'Vendedores,Administración'),       -- El código postal se guarda igual
    (525, N'Administración'),                  -- Cobrar facturas de dos empresas
    (526, N'Almacén,Administración'),          -- Ficha de producto: control de stock
    (527, N'Administración'),                  -- Facturas UE con NIF-IVA completo
    (528, N'Vendedores'),                      -- Clientes para contactar por prioridad
    (529, N'Almacén,Tiendas'),                 -- Varios códigos de barras por producto
    (530, N'Almacén,Administración'),          -- Avisos de ficha desde el almacén
    -- 1.10.39.1
    (542, N'Vendedores,Tiendas'),              -- Ofertas por producto 6+1 y 10+1 a la vez
    (543, N'Tiendas'),                         -- Recibir reposición: colores
    (544, N'Almacén,Administración'),          -- Reembolso de un envío devuelto
    (545, N'Tiendas'),                         -- Recibir reposición: buscador
    (547, N'Almacén,Vendedores'),              -- El picking dice por qué no sale un pedido
    (548, N'Administración'),                  -- Pedidos de Amazon con cantidad 0
    (552, N'Almacén,Tiendas'),                 -- Las reposiciones se rellenan solas
    (553, N'Vendedores,Tiendas'),              -- La plantilla dice el día de entrega a la agencia
    (555, N'Vendedores,Administración'),       -- Corrector del concepto del enlace de pago
    (558, N'Vendedores'),                      -- Clientes para contactar: llamadas al mes
    (559, N'Almacén'),                         -- El comparador de agencias respeta la cuarentena
    (560, N'Vendedores'),                      -- Rapports cargaba dos veces la lista
    (561, N'Tiendas')                          -- Recibir reposición: textos del servidor
    -- Para todos (se quedan a NULL): 455, 470, 490, 492, 518, 519, 524, 554, 556, 557
) v (Id, Perfiles) ON v.Id = n.Id
WHERE n.Ambito = N'Nesto' AND n.Perfiles IS NULL;

SELECT @@ROWCOUNT AS NovedadesConPerfilesAhora;
GO

-- Comprobación
SELECT Id, Version, Perfiles, Titulo
FROM dbo.Novedades
WHERE Ambito = N'Nesto' AND Version IS NOT NULL AND Fecha >= '2026-10-01'
ORDER BY Id;
