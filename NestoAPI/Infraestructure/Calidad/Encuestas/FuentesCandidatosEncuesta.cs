using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Encuestas
{
    /// <summary>
    /// NestoAPI#914 (encuestas, E1): una fuente de candidatas de UN tema por UN evento. Lo que sabe del tema se queda en su clase (nada de «si
    /// el tema es X» fuera). El lector (<see cref="LectorEncuestasSql"/>) lanza <see cref="Sql"/> con @empresa y la ventana de FECHAS DEL EVENTO
    /// [@desde, @hasta) = la de los días de candidatura menos <see cref="RetrasoDias"/>, y le pasa las filas a <see cref="Construir"/>.
    /// SQL de solo lectura: READ UNCOMMITTED y NOLOCK.
    /// </summary>
    public interface IFuenteCandidatosEncuesta
    {
        string Nombre { get; }
        TemaEncuesta Tema { get; }
        /// <summary>Tabla fuera del EDMX sin la que la fuente no se lee (null si no necesita ninguna).</summary>
        string TablaNecesaria { get; }
        /// <summary>Días desde el evento hasta que es candidata.</summary>
        int RetrasoDias(ParametrosFuentesEncuesta parametros);
        string Sql { get; }
        /// <summary>Parámetros de más que necesite el SQL (además de @empresa, @desde y @hasta).</summary>
        IDictionary<string, object> ParametrosExtra { get; }
        List<CandidataEncuesta> Construir(IDataReader filas, string empresa, int retrasoDias);
    }

    /// <summary>Lo común: leer columnas por nombre y montar la candidata (Dia = FechaEvento + retraso).</summary>
    public abstract class FuenteCandidatosEncuestaBase : IFuenteCandidatosEncuesta
    {
        internal const string CABECERA_SQL = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;\n";

        public string Nombre => GetType().Name.StartsWith("Fuente", StringComparison.Ordinal) ? GetType().Name.Substring(6) : GetType().Name;
        public abstract TemaEncuesta Tema { get; }
        public virtual string TablaNecesaria => null;
        public abstract int RetrasoDias(ParametrosFuentesEncuesta parametros);
        public abstract string Sql { get; }
        public virtual IDictionary<string, object> ParametrosExtra => new Dictionary<string, object>();

        public List<CandidataEncuesta> Construir(IDataReader filas, string empresa, int retrasoDias)
        {
            var resultado = new List<CandidataEncuesta>();
            if (filas == null)
            {
                return resultado;
            }
            while (filas.Read())
            {
                foreach (CandidataEncuesta c in DeFila(filas) ?? Enumerable.Empty<CandidataEncuesta>())
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.Cliente))
                    {
                        continue;
                    }
                    c.Empresa = empresa;
                    c.Cliente = c.Cliente.Trim();
                    c.Contacto = string.IsNullOrWhiteSpace(c.Contacto) ? "0" : c.Contacto.Trim();
                    c.Tema = Tema;
                    c.Origen = Nombre;
                    c.Dia = c.FechaEvento.Date.AddDays(retrasoDias);
                    resultado.Add(c);
                }
            }
            return resultado;
        }

        /// <summary>Las candidatas de una fila (una o varias; ninguna si la fila no sirve).</summary>
        protected abstract IEnumerable<CandidataEncuesta> DeFila(IDataRecord r);

        protected static string Texto(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v == null || v == DBNull.Value ? null : Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim();
        }

        protected static DateTime? Fecha(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v == null || v == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(v, CultureInfo.InvariantCulture);
        }

        protected static int? Entero(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v == null || v == DBNull.Value ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        protected static bool Bool(IDataRecord r, string columna)
        {
            object v = r[columna];
            return v != null && v != DBNull.Value && Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        }

        protected static string Dia(DateTime fecha) => fecha.ToString("dd/MM", CultureInfo.InvariantCulture);

        protected static string ConNombre(string producto, string nombre) =>
            string.IsNullOrWhiteSpace(nombre) ? producto : $"{nombre} ({producto})";
    }

    // ------------------------------------------------------------------ PREPARACIÓN Y ENTREGA

    /// <summary>
    /// Preparación y entrega: pedido ENTREGADO hace 2 días. Entrega confirmada = EnviosAgencia Estado 2 (Entregado) con su FechaEntrega, que
    /// solo es la fecha real en ese estado: la pone el seguimiento de la agencia (GLS, Innovatrans, CTT: <c>AplicarSeguimiento</c>) y el
    /// repartidor de la ruta propia al entregar en Ariadna. Sin notas de entrega, rectificativas ni copias; con alguna línea de producto servida.
    /// </summary>
    public class FuenteEntregaPedido : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 2;
        internal const string SQL = CABECERA_SQL + @"
SELECT e.Pedido, MAX(e.FechaEntrega) AS FechaEntrega, MAX(e.Numero) AS Envio, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto
FROM EnviosAgencia e WITH (NOLOCK)
INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = e.Empresa AND c.[Número] = e.Pedido
WHERE e.Empresa = @empresa AND e.Estado = 2 AND e.Pedido IS NOT NULL AND e.FechaEntrega >= @desde AND e.FechaEntrega < @hasta
  AND c.NotaEntrega = 0 AND c.PedidoOrigen IS NULL AND NULLIF(RTRIM(c.TipoRectificativa), '') IS NULL
  AND EXISTS (SELECT 1 FROM LinPedidoVta l WITH (NOLOCK) WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2)
GROUP BY e.Pedido, c.[Nº Cliente], c.Contacto;";

        public override TemaEncuesta Tema => TemaEncuesta.PreparacionEntrega;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? entrega = Fecha(r, "FechaEntrega");
            if (!entrega.HasValue)
            {
                yield break;
            }
            int? pedido = Entero(r, "Pedido");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Otra,
                FechaEvento = entrega.Value,
                Pedido = pedido,
                Envio = Entero(r, "Envio"),
                Motivo = $"Recibió el pedido {pedido} el {Dia(entrega.Value)}."
            };
        }
    }

    // ------------------------------------------------------------------ VENDEDOR

    /// <summary>
    /// Qué vendedores pueden ser el tema de una encuesta: una PERSONA que trata con el cliente. Fuera el vendedor general «NV»
    /// (<see cref="ComercialesPedido.EsComercialReal"/>), los que están para anular (Estado 99) y los de baja (Estado negativo). Los canales
    /// (Amazon, tienda online, público final, pedido tienda, El Edén) se quitan por cliente en <see cref="ReglaClientesEncuesta"/>. PURA.
    /// </summary>
    public static class ReglaVendedorEncuesta
    {
        public static bool EsPersona(string vendedor, int? estado) =>
            ComercialesPedido.EsComercialReal(new ComercialPedido { Vendedor = vendedor })
            && (!estado.HasValue || (estado.Value >= 0 && estado.Value != Constantes.Vendedores.ESTADO_VENDEDOR_PARA_ANULAR));

        /// <summary>
        /// Los vendedores del pedido por los que se puede preguntar, con su ámbito: LA REGLA de comisiones y factura
        /// (<see cref="ComercialesPedido.CodigosVendedores"/>): líneas PEL → el vendedor de peluquería del pedido; las demás → el de la cabecera.
        /// Peluquería sin vendedor de peluquería: nadie (como la factura), no se le pregunta al de estética por algo que no lleva.
        /// </summary>
        public static List<KeyValuePair<string, string>> VendedoresDelPedido(bool lineasPeluqueria, bool lineasOtras, string vendedorEstetica,
            string vendedorPeluqueria)
        {
            var grupos = new List<string>();
            if (lineasPeluqueria)
            {
                grupos.Add(Constantes.Productos.GRUPO_PELUQUERIA);
            }
            if (lineasOtras)
            {
                grupos.Add(Constantes.Productos.GRUPO_COSMETICA);
            }
            List<string> codigos = ComercialesPedido.CodigosVendedores(grupos, vendedorEstetica, vendedorPeluqueria, peluqueriaSinVendedorVaAlDeEstetica: false);
            return codigos.Select(c => new KeyValuePair<string, string>(c,
                lineasPeluqueria && string.Equals(c, vendedorPeluqueria?.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? AmbitosVendedorEncuesta.PELUQUERIA : AmbitosVendedorEncuesta.ESTETICA)).ToList();
        }

        public static string Nombre(string nombre, string codigo) => string.IsNullOrWhiteSpace(nombre) ? codigo?.Trim() : nombre.Trim();
    }

    /// <summary>
    /// Vendedor, primera vez: el PRIMER pedido del cliente con ese vendedor (por vendedor y ámbito: el de peluquería para las líneas PEL, el
    /// de la cabecera para el resto), 3 días después del pedido. Un pedido mixto puede dar DOS candidatas.
    /// </summary>
    public class FuenteVendedorPrimerPedido : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 3;
        internal const string SQL = CABECERA_SQL + @"
SELECT c.[Número] AS Pedido, c.Fecha, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
       RTRIM(ISNULL(c.Vendedor, '')) AS VendedorEstetica, RTRIM(ISNULL(pel.Vendedor, '')) AS VendedorPeluqueria,
       ISNULL(a.LineasPeluqueria, 0) AS LineasPeluqueria, ISNULL(a.LineasOtras, 0) AS LineasOtras,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM CabPedidoVta o WITH (NOLOCK)
                              WHERE o.Empresa = c.Empresa AND o.[Nº Cliente] = c.[Nº Cliente] AND o.Vendedor = c.Vendedor AND o.[Número] <> c.[Número]
                                AND (o.Fecha < c.Fecha OR (o.Fecha = c.Fecha AND o.[Número] < c.[Número]))) THEN 1 ELSE 0 END AS bit) AS PrevioEstetica,
       CAST(CASE WHEN pel.Vendedor IS NOT NULL AND EXISTS (SELECT 1 FROM CabPedidoVta o WITH (NOLOCK)
                              INNER JOIN VendedoresPedidoGrupoProducto vo WITH (NOLOCK) ON vo.Empresa = o.Empresa AND vo.Pedido = o.[Número] AND vo.GrupoProducto = 'PEL'
                              WHERE o.Empresa = c.Empresa AND o.[Nº Cliente] = c.[Nº Cliente] AND vo.Vendedor = pel.Vendedor AND o.[Número] <> c.[Número]
                                AND (o.Fecha < c.Fecha OR (o.Fecha = c.Fecha AND o.[Número] < c.[Número]))) THEN 1 ELSE 0 END AS bit) AS PrevioPeluqueria,
       RTRIM(ve.[Descripción]) AS NombreEstetica, ve.Estado AS EstadoEstetica, RTRIM(vp.[Descripción]) AS NombrePeluqueria, vp.Estado AS EstadoPeluqueria
FROM CabPedidoVta c WITH (NOLOCK)
OUTER APPLY (SELECT TOP 1 v.Vendedor FROM VendedoresPedidoGrupoProducto v WITH (NOLOCK)
             WHERE v.Empresa = c.Empresa AND v.Pedido = c.[Número] AND v.GrupoProducto = 'PEL') pel
CROSS APPLY (SELECT SUM(CASE WHEN l.Grupo = 'PEL' THEN 1 ELSE 0 END) AS LineasPeluqueria,
                    SUM(CASE WHEN l.Grupo = 'PEL' THEN 0 ELSE 1 END) AS LineasOtras
             FROM LinPedidoVta l WITH (NOLOCK)
             WHERE l.Empresa = c.Empresa AND l.[Número] = c.[Número] AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= -1) a
LEFT JOIN Vendedores ve WITH (NOLOCK) ON ve.Empresa = c.Empresa AND ve.[Número] = c.Vendedor
LEFT JOIN Vendedores vp WITH (NOLOCK) ON vp.Empresa = c.Empresa AND vp.[Número] = pel.Vendedor
WHERE c.Empresa = @empresa AND c.Fecha >= @desde AND c.Fecha < @hasta
  AND c.NotaEntrega = 0 AND c.PedidoOrigen IS NULL AND NULLIF(RTRIM(c.TipoRectificativa), '') IS NULL
  AND (ISNULL(a.LineasPeluqueria, 0) > 0 OR ISNULL(a.LineasOtras, 0) > 0);";

        public override TemaEncuesta Tema => TemaEncuesta.Vendedor;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            if (!fecha.HasValue)
            {
                yield break;
            }
            int? pedido = Entero(r, "Pedido");
            string estetica = Texto(r, "VendedorEstetica");
            string peluqueria = Texto(r, "VendedorPeluqueria");
            foreach (KeyValuePair<string, string> vendedor in ReglaVendedorEncuesta.VendedoresDelPedido(
                (Entero(r, "LineasPeluqueria") ?? 0) > 0, (Entero(r, "LineasOtras") ?? 0) > 0, estetica, peluqueria))
            {
                bool esPeluqueria = vendedor.Value == AmbitosVendedorEncuesta.PELUQUERIA;
                if (Bool(r, esPeluqueria ? "PrevioPeluqueria" : "PrevioEstetica")
                    || !ReglaVendedorEncuesta.EsPersona(vendedor.Key, Entero(r, esPeluqueria ? "EstadoPeluqueria" : "EstadoEstetica")))
                {
                    continue;
                }
                string nombre = ReglaVendedorEncuesta.Nombre(Texto(r, esPeluqueria ? "NombrePeluqueria" : "NombreEstetica"), vendedor.Key);
                yield return new CandidataEncuesta
                {
                    Cliente = Texto(r, "Cliente"),
                    Contacto = Texto(r, "Contacto"),
                    Clase = ClaseCandidataEncuesta.PrimeraVez,
                    FechaEvento = fecha.Value,
                    Pedido = pedido,
                    Vendedor = vendedor.Key,
                    NombreVendedor = nombre,
                    AmbitoVendedor = vendedor.Value,
                    Motivo = $"Hizo el {Dia(fecha.Value)} su primer pedido ({pedido}) con {nombre}, que le atiende {AmbitosVendedorEncuesta.Texto(vendedor.Value)}."
                };
            }
        }
    }

    /// <summary>
    /// Vendedor, cambio en la ficha (estética): Clientes.Vendedor cambiado (trgClientesUpd lo apunta en LogModificaciones: Tabla «Clientes»,
    /// Columna «Vendedor», Numero = cliente, ValorAntiguo = el de antes, Fecha). El nuevo = el que tiene HOY la ficha (el rastro no guarda el
    /// nuevo) y solo de los contactos tocados desde entonces (su [Fecha Modificación]; el rastro no dice el contacto). 14 días después, para
    /// que haya podido tratar con él. Un cliente con varios contactos cambiados da una candidata por vendedor.
    /// </summary>
    public class FuenteVendedorCambioFicha : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 14;
        internal const string SQL = CABECERA_SQL + @"
SELECT RTRIM(l.Numero) AS Cliente, MIN(RTRIM(c.Contacto)) AS Contacto, MAX(l.Fecha) AS Fecha, RTRIM(c.Vendedor) AS Vendedor,
       MAX(RTRIM(ISNULL(l.ValorAntiguo, ''))) AS VendedorAnterior, MAX(RTRIM(v.[Descripción])) AS NombreVendedor, MAX(v.Estado) AS EstadoVendedor
FROM LogModificaciones l WITH (NOLOCK)
INNER JOIN Clientes c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.[Nº Cliente] = l.Numero
LEFT JOIN Vendedores v WITH (NOLOCK) ON v.Empresa = c.Empresa AND v.[Número] = c.Vendedor
WHERE l.Empresa = @empresa AND l.Tabla = 'Clientes' AND l.Columna = 'Vendedor' AND l.Fecha >= @desde AND l.Fecha < @hasta
  AND ISNULL(c.Estado, 0) >= 0 AND c.Vendedor IS NOT NULL AND RTRIM(c.Vendedor) <> RTRIM(ISNULL(l.ValorAntiguo, ''))
  AND c.[Fecha Modificación] >= DATEADD(minute, -5, l.Fecha)
GROUP BY l.Numero, c.Vendedor;";

        public override TemaEncuesta Tema => TemaEncuesta.Vendedor;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            string vendedor = Texto(r, "Vendedor");
            if (!fecha.HasValue || !ReglaVendedorEncuesta.EsPersona(vendedor, Entero(r, "EstadoVendedor")))
            {
                yield break;
            }
            string nombre = ReglaVendedorEncuesta.Nombre(Texto(r, "NombreVendedor"), vendedor);
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Cambio,
                FechaEvento = fecha.Value,
                Vendedor = vendedor,
                NombreVendedor = nombre,
                AmbitoVendedor = AmbitosVendedorEncuesta.ESTETICA,
                Motivo = $"Desde el {Dia(fecha.Value)} le atiende {nombre} {AmbitosVendedorEncuesta.Texto(AmbitosVendedorEncuesta.ESTETICA)}."
            };
        }
    }

    /// <summary>
    /// Vendedor, cambio en la ficha (peluquería): VendedoresClienteGrupoProducto (PEL) NO deja rastro del cambio; lo que hay es su
    /// FechaModificacion. Se toma como cambio si su vendedor es distinto del vendedor de peluquería del último pedido del cliente ANTERIOR a
    /// esa fecha (VendedoresPedidoGrupoProducto PEL). Sin pedido de peluquería anterior no se sabe si es un cambio: no sale. 14 días después.
    /// </summary>
    public class FuenteVendedorPeluqueriaCambioFicha : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 14;
        internal const string SQL = CABECERA_SQL + @"
SELECT RTRIM(v.Cliente) AS Cliente, RTRIM(v.Contacto) AS Contacto, v.FechaModificacion AS Fecha, RTRIM(v.Vendedor) AS Vendedor,
       RTRIM(ant.Vendedor) AS VendedorAnterior, RTRIM(ve.[Descripción]) AS NombreVendedor, ve.Estado AS EstadoVendedor
FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
CROSS APPLY (SELECT TOP 1 vo.Vendedor
             FROM CabPedidoVta o WITH (NOLOCK)
             INNER JOIN VendedoresPedidoGrupoProducto vo WITH (NOLOCK) ON vo.Empresa = o.Empresa AND vo.Pedido = o.[Número] AND vo.GrupoProducto = 'PEL'
             WHERE o.Empresa = v.Empresa AND o.[Nº Cliente] = v.Cliente AND o.Fecha < v.FechaModificacion
             ORDER BY o.Fecha DESC, o.[Número] DESC) ant
LEFT JOIN Vendedores ve WITH (NOLOCK) ON ve.Empresa = v.Empresa AND ve.[Número] = v.Vendedor
WHERE v.Empresa = @empresa AND v.GrupoProducto = 'PEL' AND v.FechaModificacion >= @desde AND v.FechaModificacion < @hasta
  AND RTRIM(ant.Vendedor) <> RTRIM(v.Vendedor);";

        public override TemaEncuesta Tema => TemaEncuesta.Vendedor;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            string vendedor = Texto(r, "Vendedor");
            if (!fecha.HasValue || !ReglaVendedorEncuesta.EsPersona(vendedor, Entero(r, "EstadoVendedor"))
                || string.Equals(vendedor, Texto(r, "VendedorAnterior"), StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
            string nombre = ReglaVendedorEncuesta.Nombre(Texto(r, "NombreVendedor"), vendedor);
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Cambio,
                FechaEvento = fecha.Value,
                Vendedor = vendedor,
                NombreVendedor = nombre,
                AmbitoVendedor = AmbitosVendedorEncuesta.PELUQUERIA,
                Motivo = $"Desde el {Dia(fecha.Value)} le atiende {nombre} {AmbitosVendedorEncuesta.Texto(AmbitosVendedorEncuesta.PELUQUERIA)}."
            };
        }
    }

    // ------------------------------------------------------------------ CÓMO RESOLVIMOS UN PROBLEMA

    /// <summary>
    /// Problema resuelto: la NOTA DE ENTREGA de lo que quedó pendiente (CreadorNotaEntregaPendiente #542: CabPedidoVta NotaEntrega = 1 con
    /// PedidoOrigen) ya ENTREGADA (su envío en Estado 2 con FechaEntrega), 2 días después. Enlaza con el pedido original y el envío.
    /// </summary>
    public class FuenteProblemaNotaEntregaPendiente : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 2;
        internal const string SQL = CABECERA_SQL + @"
SELECT n.PedidoOrigen AS Pedido, n.[Número] AS Nota, MAX(e.FechaEntrega) AS FechaEntrega, MAX(e.Numero) AS Envio,
       RTRIM(n.[Nº Cliente]) AS Cliente, RTRIM(n.Contacto) AS Contacto
FROM CabPedidoVta n WITH (NOLOCK)
INNER JOIN EnviosAgencia e WITH (NOLOCK) ON e.Empresa = n.Empresa AND e.Pedido = n.[Número]
WHERE n.Empresa = @empresa AND n.NotaEntrega = 1 AND n.PedidoOrigen IS NOT NULL
  AND e.Estado = 2 AND e.FechaEntrega >= @desde AND e.FechaEntrega < @hasta
GROUP BY n.PedidoOrigen, n.[Número], n.[Nº Cliente], n.Contacto;";

        public override TemaEncuesta Tema => TemaEncuesta.ProblemaResuelto;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? entrega = Fecha(r, "FechaEntrega");
            if (!entrega.HasValue)
            {
                yield break;
            }
            int? pedido = Entero(r, "Pedido");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.ProblemaResuelto,
                FechaEvento = entrega.Value,
                Pedido = pedido,
                Envio = Entero(r, "Envio"),
                Motivo = $"El {Dia(entrega.Value)} recibió lo que se quedó pendiente de entregar del pedido {pedido}."
            };
        }
    }

    /// <summary>
    /// Problema resuelto: un envío devuelto o incidentado que se decidió REENVIAR (dbo.EnviosAgenciaReenvios, #699) y cuyo reenvío ya está
    /// ENTREGADO (Estado 2 con FechaEntrega), 2 días después. Sin la tabla, no se lee.
    /// </summary>
    public class FuenteProblemaReenvioEntregado : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 2;
        internal const string SQL = CABECERA_SQL + @"
SELECT o.Pedido, n.Numero AS Envio, o.Numero AS EnvioOrigen, n.FechaEntrega, RTRIM(o.Cliente) AS Cliente, RTRIM(o.Contacto) AS Contacto
FROM dbo.EnviosAgenciaReenvios r WITH (NOLOCK)
INNER JOIN EnviosAgencia n WITH (NOLOCK) ON n.Numero = r.EnvioNuevo
INNER JOIN EnviosAgencia o WITH (NOLOCK) ON o.Numero = r.EnvioOrigen
WHERE r.Decision = 'Reenviar' AND n.Empresa = @empresa AND n.Estado = 2 AND n.FechaEntrega >= @desde AND n.FechaEntrega < @hasta;";

        public override TemaEncuesta Tema => TemaEncuesta.ProblemaResuelto;
        public override string TablaNecesaria => "dbo.EnviosAgenciaReenvios";
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? entrega = Fecha(r, "FechaEntrega");
            if (!entrega.HasValue)
            {
                yield break;
            }
            int? pedido = Entero(r, "Pedido");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.ProblemaResuelto,
                FechaEvento = entrega.Value,
                Pedido = pedido,
                Envio = Entero(r, "Envio"),
                Motivo = $"El {Dia(entrega.Value)} recibió el reenvío del pedido {pedido} después de una incidencia con la agencia."
            };
        }
    }

    /// <summary>
    /// Problema resuelto: entrega incompleta del reparto propio (#880, dbo.RepartoBultosPendientes) con TODOS sus bultos cerrados (entregado
    /// en mano, perdido o abonado), 2 días después del último cierre. Sin la tabla, no se lee.
    /// </summary>
    public class FuenteProblemaBultosPendientesCerrados : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 2;
        internal const string SQL = CABECERA_SQL + @"
SELECT b.EnvioId AS Envio, MAX(b.Pedido) AS Pedido, MAX(RTRIM(b.Cliente)) AS Cliente, MAX(RTRIM(b.Contacto)) AS Contacto,
       MAX(b.FechaCierre) AS FechaCierre, MAX(RTRIM(b.Cierre)) AS Cierre
FROM dbo.RepartoBultosPendientes b WITH (NOLOCK)
WHERE (b.Empresa = @empresa OR b.Empresa IS NULL)
GROUP BY b.EnvioId
HAVING SUM(CASE WHEN b.Estado = 'Pendiente' THEN 1 ELSE 0 END) = 0 AND MAX(b.FechaCierre) >= @desde AND MAX(b.FechaCierre) < @hasta;";

        public override TemaEncuesta Tema => TemaEncuesta.ProblemaResuelto;
        public override string TablaNecesaria => "dbo.RepartoBultosPendientes";
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? cierre = Fecha(r, "FechaCierre");
            if (!cierre.HasValue)
            {
                yield break;
            }
            int? pedido = Entero(r, "Pedido");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.ProblemaResuelto,
                FechaEvento = cierre.Value,
                Pedido = pedido,
                Envio = Entero(r, "Envio"),
                Motivo = $"El {Dia(cierre.Value)} quedó resuelto lo que faltó en la entrega" + (pedido.HasValue ? $" del pedido {pedido}." : ".")
            };
        }
    }

    /// <summary>
    /// Problema resuelto: una factura RECTIFICATIVA (abono o rectificación) hecha: CabFacturaVta con filas en LinFacturaVtaRectificacion
    /// (NumeroFactura = ella; FacturaOriginalNumero = la rectificada), 3 días después de su fecha. La misma fuente que los motivos
    /// FacturaRectificadaEntera/EnParte del pedido perfecto.
    /// </summary>
    public class FuenteProblemaRectificativa : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 3;
        internal const string SQL = CABECERA_SQL + @"
SELECT RTRIM(f.[Número]) AS Factura, f.Fecha, RTRIM(f.[Nº Cliente]) AS Cliente, RTRIM(f.Contacto) AS Contacto,
       MIN(RTRIM(x.FacturaOriginalNumero)) AS FacturaOriginal
FROM CabFacturaVta f WITH (NOLOCK)
INNER JOIN LinFacturaVtaRectificacion x WITH (NOLOCK) ON x.Empresa = f.Empresa AND x.NumeroFactura = f.[Número]
WHERE f.Empresa = @empresa AND f.Fecha >= @desde AND f.Fecha < @hasta
GROUP BY f.[Número], f.Fecha, f.[Nº Cliente], f.Contacto;";

        public override TemaEncuesta Tema => TemaEncuesta.ProblemaResuelto;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            if (!fecha.HasValue)
            {
                yield break;
            }
            string original = Texto(r, "FacturaOriginal");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.ProblemaResuelto,
                FechaEvento = fecha.Value,
                Factura = Texto(r, "Factura"),
                Motivo = $"El {Dia(fecha.Value)} le hicimos la factura rectificativa {Texto(r, "Factura")}" +
                    (string.IsNullOrWhiteSpace(original) ? "." : $" de la factura {original}.")
            };
        }
    }

    // ------------------------------------------------------------------ ADMINISTRACIÓN Y COBRO

    /// <summary>
    /// Administración y cobro, primera vez: el PRIMER recibo del cliente girado en una remesa (ExtractoCliente cartera, TipoApunte 2, con
    /// Remesa; el día es el de la remesa, Remesas.Fecha, como ModeloImpagados), sin ningún otro girado en una remesa anterior. 3 días después.
    /// El cambio de forma de pago de la ficha NO sale: CondPagoCliente no tiene fecha ni deja rastro (ver docs).
    /// </summary>
    public class FuenteCobroPrimerGiro : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 3;
        internal const string SQL = CABECERA_SQL + @"
SELECT RTRIM(e.[Número]) AS Cliente, MIN(RTRIM(e.Contacto)) AS Contacto, MIN(r.Fecha) AS Fecha, MIN(e.Remesa) AS Remesa,
       MIN(RTRIM(e.[Nº Documento])) AS Factura
FROM ExtractoCliente e WITH (NOLOCK)
INNER JOIN Remesas r WITH (NOLOCK) ON r.Empresa = e.Empresa AND r.[Número] = e.Remesa
WHERE e.Empresa = @empresa AND e.TipoApunte = '2' AND e.Importe > 0 AND r.Fecha >= @desde AND r.Fecha < @hasta
  AND NOT EXISTS (SELECT 1 FROM ExtractoCliente e2 WITH (NOLOCK)
                  INNER JOIN Remesas r2 WITH (NOLOCK) ON r2.Empresa = e2.Empresa AND r2.[Número] = e2.Remesa
                  WHERE e2.Empresa = e.Empresa AND e2.[Número] = e.[Número] AND e2.TipoApunte = '2' AND e2.Importe > 0
                    AND r2.Fecha < CAST(CAST(r.Fecha AS date) AS datetime))
GROUP BY e.[Número];";

        public override TemaEncuesta Tema => TemaEncuesta.AdministracionCobro;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            if (!fecha.HasValue)
            {
                yield break;
            }
            string factura = Texto(r, "Factura");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.PrimeraVez,
                FechaEvento = fecha.Value,
                Factura = factura,
                Motivo = $"El {Dia(fecha.Value)} le pasamos al banco su primer recibo" + (string.IsNullOrWhiteSpace(factura) ? "." : $" (factura {factura}).")
            };
        }
    }

    // ------------------------------------------------------------------ INFORMACIÓN TÉCNICA (PROFESORAS)

    /// <summary>
    /// Información técnica: un APARATO ya entregado (Carlos 27/11/26: las profesoras intervienen sobre todo con la venta de cursos y de
    /// aparatos; la puesta en marcha es después). Aparato = línea de producto del grupo «APA» (<see cref="Constantes.Productos.GRUPO_APARATOS"/>,
    /// el que ya usan las reglas de muestras y de ruta); entrega confirmada como en <see cref="FuenteEntregaPedido"/>. N días después de la
    /// entrega (ParametrosUsuario «(defecto)» Calidad.EncuestasDiasTrasAparato, 15 sin fila).
    /// </summary>
    public class FuenteTecnicaAparato : FuenteCandidatosEncuestaBase
    {
        internal const string SQL = CABECERA_SQL + @"
SELECT t.Pedido, t.FechaEntrega, t.Envio, t.Cliente, t.Contacto, t.Producto, RTRIM(p.Nombre) AS NombreProducto
FROM (SELECT e.Pedido, MAX(e.FechaEntrega) AS FechaEntrega, MAX(e.Numero) AS Envio, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto,
             MIN(RTRIM(l.Producto)) AS Producto
      FROM EnviosAgencia e WITH (NOLOCK)
      INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = e.Empresa AND c.[Número] = e.Pedido
      INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = c.Empresa AND l.[Número] = c.[Número]
      WHERE e.Empresa = @empresa AND e.Estado = 2 AND e.Pedido IS NOT NULL AND e.FechaEntrega >= @desde AND e.FechaEntrega < @hasta
        AND c.NotaEntrega = 0 AND c.PedidoOrigen IS NULL AND NULLIF(RTRIM(c.TipoRectificativa), '') IS NULL
        AND l.TipoLinea = 1 AND l.Cantidad > 0 AND l.Estado >= 2 AND l.Grupo = @grupoAparatos
      GROUP BY e.Pedido, c.[Nº Cliente], c.Contacto) t
LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @empresa AND p.[Número] = t.Producto;";

        public override TemaEncuesta Tema => TemaEncuesta.InformacionTecnica;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) =>
            parametros?.DiasTrasAparato ?? ParametrosEncuestas.DIAS_TRAS_APARATO_POR_DEFECTO;
        public override string Sql => SQL;
        public override IDictionary<string, object> ParametrosExtra =>
            new Dictionary<string, object> { ["@grupoAparatos"] = Constantes.Productos.GRUPO_APARATOS };

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? entrega = Fecha(r, "FechaEntrega");
            if (!entrega.HasValue)
            {
                yield break;
            }
            string producto = Texto(r, "Producto");
            string nombre = Texto(r, "NombreProducto");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Otra,
                FechaEvento = entrega.Value,
                Pedido = Entero(r, "Pedido"),
                Envio = Entero(r, "Envio"),
                Producto = producto,
                NombreProducto = nombre,
                Motivo = $"Recibió el aparato {ConNombre(producto, nombre)} el {Dia(entrega.Value)} y ya lleva unos días con él."
            };
        }
    }

    /// <summary>
    /// Información técnica: una FORMACIÓN SIN EVENTO facturada (grupo «CUR» que no es la referencia de un evento ni la plantilla:
    /// <see cref="ReglasFormacionPlantilla.EsFormacion"/>; tampoco el ProductoCurso de ningún evento). Los cursos CON evento se preguntan por la
    /// asistencia (<see cref="FuenteTecnicaAsistenciaEvento"/>). N días después de la factura (Calidad.EncuestasDiasTrasFormacion, 7 sin fila).
    /// </summary>
    public class FuenteTecnicaFormacionSinEvento : FuenteCandidatosEncuestaBase
    {
        internal const string SQL = CABECERA_SQL + @"
SELECT t.Pedido, t.FechaFactura, t.Factura, t.Cliente, t.Contacto, t.Producto, RTRIM(p.Nombre) AS NombreProducto
FROM (SELECT l.[Número] AS Pedido, MIN(l.[Fecha Factura]) AS FechaFactura, MIN(RTRIM(l.[Nº Factura])) AS Factura,
             RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Contacto) AS Contacto, MIN(RTRIM(l.Producto)) AS Producto
      FROM LinPedidoVta l WITH (NOLOCK)
      WHERE l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Grupo = @grupoCursos AND l.Cantidad > 0 AND l.Estado >= 4
        AND l.[Fecha Factura] >= @desde AND l.[Fecha Factura] < @hasta
        AND l.Producto NOT LIKE 'EVENTO%' AND RTRIM(l.Producto) <> @plantilla
        AND NOT EXISTS (SELECT 1 FROM Eventos ev WITH (NOLOCK) WHERE ev.ProductoCurso = l.Producto)
      GROUP BY l.[Número], l.[Nº Cliente], l.Contacto) t
LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @empresa AND p.[Número] = t.Producto;";

        public override TemaEncuesta Tema => TemaEncuesta.InformacionTecnica;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) =>
            parametros?.DiasTrasFormacion ?? ParametrosEncuestas.DIAS_TRAS_FORMACION_POR_DEFECTO;
        public override string Sql => SQL;
        public override IDictionary<string, object> ParametrosExtra => new Dictionary<string, object>
        {
            ["@grupoCursos"] = Constantes.Productos.GRUPO_CURSOS,
            ["@plantilla"] = ReglasProductoCurso.Plantilla
        };

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? factura = Fecha(r, "FechaFactura");
            string producto = Texto(r, "Producto");
            if (!factura.HasValue || !ReglasFormacionPlantilla.EsFormacion(producto, Constantes.Productos.GRUPO_CURSOS))
            {
                yield break;
            }
            string nombre = Texto(r, "NombreProducto");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Otra,
                FechaEvento = factura.Value,
                Pedido = Entero(r, "Pedido"),
                Factura = Texto(r, "Factura"),
                Producto = producto,
                NombreProducto = nombre,
                Motivo = $"Hizo la formación {ConNombre(producto, nombre)} (facturada el {Dia(factura.Value)})."
            };
        }
    }

    /// <summary>
    /// Información técnica: ASISTIÓ a un evento (curso, masterclass; de pago o gratuito): EventosInscripciones Estado «Asistio» (la lista
    /// pasada de #908, o la que el job da por asistida en los online), 2 días después de Eventos.Fecha. La profesora = Eventos.Ponente.
    /// </summary>
    public class FuenteTecnicaAsistenciaEvento : FuenteCandidatosEncuestaBase
    {
        public const int RETRASO = 2;
        internal const string SQL = CABECERA_SQL + @"
SELECT ev.Id AS EventoId, ev.Fecha, RTRIM(ev.Titulo) AS Titulo, RTRIM(ISNULL(ev.Ponente, '')) AS Ponente,
       RTRIM(ISNULL(ev.ProductoCurso, '')) AS Producto, RTRIM(i.Cliente) AS Cliente, RTRIM(i.Contacto) AS Contacto
FROM EventosInscripciones i WITH (NOLOCK)
INNER JOIN Eventos ev WITH (NOLOCK) ON ev.Id = i.EventoId
WHERE ev.Empresa = @empresa AND i.Estado = @asistio AND i.Cliente IS NOT NULL AND ev.Fecha >= @desde AND ev.Fecha < @hasta;";

        public override TemaEncuesta Tema => TemaEncuesta.InformacionTecnica;
        public override int RetrasoDias(ParametrosFuentesEncuesta parametros) => RETRASO;
        public override string Sql => SQL;
        public override IDictionary<string, object> ParametrosExtra =>
            new Dictionary<string, object> { ["@asistio"] = EstadoInscripcionEvento.Asistio.ToString() };

        protected override IEnumerable<CandidataEncuesta> DeFila(IDataRecord r)
        {
            DateTime? fecha = Fecha(r, "Fecha");
            if (!fecha.HasValue)
            {
                yield break;
            }
            string titulo = Texto(r, "Titulo");
            string ponente = Texto(r, "Ponente");
            string producto = Texto(r, "Producto");
            yield return new CandidataEncuesta
            {
                Cliente = Texto(r, "Cliente"),
                Contacto = Texto(r, "Contacto"),
                Clase = ClaseCandidataEncuesta.Otra,
                FechaEvento = fecha.Value,
                EventoId = Entero(r, "EventoId"),
                Producto = string.IsNullOrWhiteSpace(producto) ? null : producto,
                NombreProducto = titulo,
                Profesora = string.IsNullOrWhiteSpace(ponente) ? null : ponente,
                Motivo = $"Asistió al curso «{titulo}» el {Dia(fecha.Value)}."
            };
        }
    }

    /// <summary>Todas las fuentes del corte E1 (una por evento; un tema nuevo = una clase nueva aquí).</summary>
    public static class FuentesCandidatosEncuesta
    {
        public static IReadOnlyList<IFuenteCandidatosEncuesta> Todas() => new List<IFuenteCandidatosEncuesta>
        {
            new FuenteEntregaPedido(),
            new FuenteVendedorPrimerPedido(),
            new FuenteVendedorCambioFicha(),
            new FuenteVendedorPeluqueriaCambioFicha(),
            new FuenteProblemaNotaEntregaPendiente(),
            new FuenteProblemaReenvioEntregado(),
            new FuenteProblemaBultosPendientesCerrados(),
            new FuenteProblemaRectificativa(),
            new FuenteCobroPrimerGiro(),
            new FuenteTecnicaAparato(),
            new FuenteTecnicaFormacionSinEvento(),
            new FuenteTecnicaAsistenciaEvento()
        };
    }

    /// <summary>
    /// Clientes a los que nunca se encuesta: los ficticios de canal o de venta a consumidor final
    /// (<see cref="Constantes.ClientesEspeciales.EsClienteFacturaSimplificada"/>: tienda online, Amazon, público final, materiales de cursos,
    /// pedido tienda) y El Edén (interno). PURA.
    /// </summary>
    public static class ReglaClientesEncuesta
    {
        public static bool Excluido(string cliente) =>
            string.IsNullOrWhiteSpace(cliente)
            || Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(cliente)
            || string.Equals(cliente.Trim(), Constantes.ClientesEspeciales.EL_EDEN, StringComparison.OrdinalIgnoreCase);
    }
}
