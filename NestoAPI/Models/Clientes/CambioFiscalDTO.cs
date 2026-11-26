using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Clientes
{
    /// <summary>
    /// NestoAPI#876 (corte nuevo, Carlos 26/11/26): los bloques que se pueden copiar del cliente anterior al nuevo cuando un
    /// cliente cambia de forma fiscal. Nunca lo fiscal (NIF, nombre), ni el IBAN ni el mandato.
    /// </summary>
    public static class BloquesCambioFiscal
    {
        /// <summary>Estado, vendedor y vendedores por grupo de producto.</summary>
        public const string COMERCIAL = "Comercial";
        /// <summary>Solo el tipo de IVA / recargo de equivalencia de la ficha.</summary>
        public const string FISCALIDAD = "Fiscalidad";
        /// <summary>Forma y plazos de pago por importe mínimo (CondPagoClientes). Ni el IBAN ni el mandato.</summary>
        public const string CONDICIONES_PAGO = "CondicionesPago";
        /// <summary>Las filas de DescuentosProducto del cliente (sin duplicar las que ya tenga iguales).</summary>
        public const string DESCUENTOS = "Descuentos";
        /// <summary>Las OfertasPermitidas propias del cliente.</summary>
        public const string OFERTAS = "Ofertas";
        /// <summary>Comentarios, días de servir, ruta, periodo de facturación, NoComisiona y MantenerJunto.</summary>
        public const string FICHA = "Ficha";

        public static readonly IReadOnlyList<string> TODOS = new[] { COMERCIAL, FISCALIDAD, CONDICIONES_PAGO, DESCUENTOS, OFERTAS, FICHA };

        public static string Nombre(string bloque)
        {
            switch (bloque)
            {
                case COMERCIAL: return "Estado y vendedores";
                case FISCALIDAD: return "Tipo de IVA";
                case CONDICIONES_PAGO: return "Forma y plazos de pago";
                case DESCUENTOS: return "Descuentos pactados";
                case OFERTAS: return "Ofertas permitidas";
                case FICHA: return "Comentarios, días de servir, ruta y facturación";
                default: return bloque;
            }
        }

        /// <summary>
        /// Los bloques pedidos con su nombre canónico (sin mayúsculas). Null o vacío = todos. Los que no existen van a
        /// <paramref name="desconocidos"/>.
        /// </summary>
        public static List<string> Normalizar(IEnumerable<string> pedidos, out List<string> desconocidos)
        {
            desconocidos = new List<string>();
            List<string> lista = (pedidos ?? Enumerable.Empty<string>()).Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList();
            if (!lista.Any())
            {
                return TODOS.ToList();
            }
            List<string> validos = new List<string>();
            foreach (string b in lista)
            {
                string canonico = TODOS.FirstOrDefault(t => string.Equals(t, b, StringComparison.OrdinalIgnoreCase));
                if (canonico == null)
                {
                    desconocidos.Add(b);
                }
                else if (!validos.Contains(canonico))
                {
                    validos.Add(canonico);
                }
            }
            return TODOS.Where(validos.Contains).ToList();
        }
    }

    /// <summary>NestoAPI#876: cuerpo de <c>POST api/Clientes/CambioFiscal/Copiar</c>.</summary>
    public class CopiaCambioFiscalRequest
    {
        public string Empresa { get; set; }
        public string ClienteAnterior { get; set; }
        public string ClienteNuevo { get; set; }
        /// <summary>Qué bloques (<see cref="BloquesCambioFiscal"/>). Null o vacío = todos.</summary>
        public List<string> Bloques { get; set; }
        /// <summary>Solo para auditoría si no viene del token.</summary>
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#876: un bloque, con lo que tiene cada cliente y lo que cambiaría (o ha cambiado) en el nuevo.</summary>
    public class BloqueCambioFiscalDTO
    {
        public string Bloque { get; set; }
        public string Nombre { get; set; }
        public List<string> Anterior { get; set; } = new List<string>();
        public List<string> Nuevo { get; set; } = new List<string>();
        /// <summary>Lo que se copia («Contacto 0: IVA G21 → E52»). Vacío = el nuevo ya lo tiene igual.</summary>
        public List<string> Cambios { get; set; } = new List<string>();
        /// <summary>Lo que no se copia y por qué, o lo que hay que hacer a mano (la cuenta bancaria…).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
        public bool HayCambios => Cambios.Any();
    }

    /// <summary>NestoAPI#876: un pedido pendiente del cliente anterior, para preguntar si pasa al nuevo.</summary>
    public class PedidoPendienteCambioFiscalDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Contacto { get; set; }
        public DateTime? Fecha { get; set; }
        /// <summary>Base imponible de lo pendiente (líneas -1..1 con cantidad, sin albarán).</summary>
        public decimal Importe { get; set; }
        public decimal Total { get; set; }
        public int Lineas { get; set; }
        public bool TienePicking { get; set; }
        public List<int> Pickings { get; set; } = new List<int>();
        public bool NotaEntrega { get; set; }
        /// <summary>Si se puede intentar pasar. Con picking, true: se decide al pasarlo (solo si no cambia nada; el ensayo lo dice).</summary>
        public bool SePuedePasar { get; set; }
        /// <summary>Por qué no se puede, o lo que hay que saber (con picking).</summary>
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#876: respuesta de <c>GET api/Clientes/CambioFiscal/Previa</c> y de <c>POST …/Copiar</c>.</summary>
    public class CopiaCambioFiscalDTO
    {
        public bool Ensayo { get; set; }
        public string Empresa { get; set; }
        public string ClienteAnterior { get; set; }
        public string NombreAnterior { get; set; }
        public string ClienteNuevo { get; set; }
        public string NombreNuevo { get; set; }
        /// <summary>Ya consta en ClientesAnteriores que el nuevo es el anterior con otra forma fiscal.</summary>
        public bool Enlazados { get; set; }
        /// <summary>No consta, pero el nuevo tiene origen CAMBIO-FISCAL: el enlace se crea al copiar.</summary>
        public bool SeEnlazaAlCopiar { get; set; }
        /// <summary>Por qué no se puede copiar (no son el mismo cliente con otra forma fiscal…). Null = se puede.</summary>
        public string MotivoNoSePuedeCopiar { get; set; }
        public List<BloqueCambioFiscalDTO> Bloques { get; set; } = new List<BloqueCambioFiscalDTO>();
        public bool HayCambios => Bloques.Any(b => b.HayCambios);
        /// <summary>Solo en la previa: los pedidos pendientes del anterior.</summary>
        public List<PedidoPendienteCambioFiscalDTO> PedidosPendientes { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#876: cuerpo de <c>POST api/Clientes/CambioFiscal/PasarPedidos</c>.</summary>
    public class PasarPedidosCambioFiscalRequest
    {
        public string Empresa { get; set; }
        public string ClienteAnterior { get; set; }
        public string ClienteNuevo { get; set; }
        public List<int> Pedidos { get; set; }
        /// <summary>Como en CambiarCliente: seguir aunque el pedido no pase la validación (si quien lo pide puede).</summary>
        public bool CreadoSinPasarValidacion { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#876: cómo ha ido (o iría) cada pedido.</summary>
    public class PedidoPasadoCambioFiscalDTO
    {
        public int Pedido { get; set; }
        /// <summary>Se ha pasado (o, en el ensayo, se pasaría).</summary>
        public bool Pasado { get; set; }
        public bool TeniaPicking { get; set; }
        public bool SinCambiosDeImportes { get; set; }
        /// <summary>Código del rechazo (p. ej. CAMBIO_CLIENTE_CON_PICKING_CAMBIA, NO_ES_DEL_CLIENTE_ANTERIOR, NO_SE_PUEDE).</summary>
        public string Codigo { get; set; }
        public string Mensaje { get; set; }
        public List<string> Cambios { get; set; } = new List<string>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#876: respuesta de <c>POST api/Clientes/CambioFiscal/PasarPedidos</c>.</summary>
    public class PasarPedidosCambioFiscalDTO
    {
        public bool Ensayo { get; set; }
        public string ClienteAnterior { get; set; }
        public string ClienteNuevo { get; set; }
        public List<PedidoPasadoCambioFiscalDTO> Pedidos { get; set; } = new List<PedidoPasadoCambioFiscalDTO>();
        public int Pasados => Pedidos.Count(p => p.Pasado);
        public int Rechazados => Pedidos.Count(p => !p.Pasado);
    }
}
