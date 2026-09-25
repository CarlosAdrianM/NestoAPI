using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#542: un modo de facturación y si se puede elegir para el pedido (y por qué no, si no).</summary>
    public class ModoFacturacionPermitidoDTO
    {
        public byte Modo { get; set; }
        public string Nombre { get; set; }
        public bool Permitido { get; set; }
        /// <summary>Por qué NO se puede elegir, en lenguaje de usuario. Null si está permitido.</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#542: qué modos de facturación tienen sentido para un pedido, para que Nesto y NestoApp no
    /// dejen elegir los que no. La única regla de negocio que hay hoy es la de los plazos, que viven en los
    /// triggers trgCabPedidoVtaIns/Upd: si los plazos de pago del pedido no son ninguno de los de la ficha
    /// del cliente (CondPagoClientes del contacto de cobro), y no son contado ni CR, y el periodo no es fin
    /// de mes, el pedido se factura de una vez (MantenerJunto = 1), para que los efectos no se queden
    /// pequeños. Con modos: eso solo prohíbe «por entregas»; «al completar» y «todo ahora» facturan de una
    /// vez y valen igual. Una nota de entrega no se factura, así que no tiene modo.
    /// </summary>
    public static class ModosFacturacionPermitidos
    {
        internal const string MOTIVO_PLAZOS = "Los plazos de pago no son los de la ficha del cliente: el pedido se factura de una vez, al completarlo o todo ahora.";
        internal const string MOTIVO_NOTA_ENTREGA = "Una nota de entrega no se factura.";

        private static readonly byte[] TODOS =
        {
            Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS,
            Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR,
            Constantes.Pedidos.ModosFacturacion.TODO_AHORA_Y_LO_PENDIENTE_DESPUES
        };

        public class Sugerencia
        {
            public byte Modo { get; set; }
            public string Nombre { get; set; }
            public string Motivo { get; set; }
            /// <summary>Los modos que se pueden elegir para este pedido (el sugerido siempre está).</summary>
            public List<byte> ModosPermitidos { get; set; } = new List<byte>();
            /// <summary>Los tres modos con su permiso y, si no se puede, el motivo para enseñarlo.</summary>
            public List<ModoFacturacionPermitidoDTO> Modos { get; set; } = new List<ModoFacturacionPermitidoDTO>();
        }

        /// <summary>
        /// La condición literal de los triggers: plazos que no son de la ficha, salvo contado, CR y fin de mes.
        /// <paramref name="plazosSonDeLaFicha"/> lo decide quien tiene la BD (CondPagoClientes del contacto de cobro).
        /// </summary>
        public static bool PlazosExigenUnaSolaFactura(string plazosPago, string periodoFacturacion, bool plazosSonDeLaFicha)
        {
            if (plazosSonDeLaFicha)
            {
                return false;
            }
            string plazos = plazosPago?.Trim() ?? string.Empty;
            if (string.Equals(plazos, Constantes.PlazosPago.CONTADO, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(plazos, Constantes.PlazosPago.CONTADO_RIGUROSO, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return !string.Equals(periodoFacturacion?.Trim(), Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>El núcleo puro: los tres modos con su permiso y el motivo de los que no.</summary>
        public static List<ModoFacturacionPermitidoDTO> Calcular(bool unaSolaFactura, bool notaEntrega)
        {
            return TODOS.Select(modo => new ModoFacturacionPermitidoDTO
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosFacturacion.Nombre(modo),
                Motivo = MotivoNoPermitido(modo, unaSolaFactura, notaEntrega)
            })
            .Select(m => { m.Permitido = m.Motivo == null; return m; })
            .ToList();
        }

        private static string MotivoNoPermitido(byte modo, bool unaSolaFactura, bool notaEntrega)
        {
            if (notaEntrega)
            {
                return MOTIVO_NOTA_ENTREGA;
            }
            if (unaSolaFactura && modo == Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS)
            {
                return MOTIVO_PLAZOS;
            }
            return null;
        }

        public static List<byte> Permitidos(IEnumerable<ModoFacturacionPermitidoDTO> modos)
            => (modos ?? Enumerable.Empty<ModoFacturacionPermitidoDTO>()).Where(m => m.Permitido).Select(m => m.Modo).ToList();

        /// <summary>
        /// El modo a proponer: el que rige hoy en el pedido (el informado o, si no, el derivado de
        /// mantenerJunto sobre el guardado) si se puede elegir; si no, «al completar», que es lo que los
        /// triggers hacían hasta ahora con esos plazos.
        /// </summary>
        public static Sugerencia Sugerir(PedidoVentaDTO pedido, byte? modoAlmacenado, bool plazosSonDeLaFicha)
        {
            bool unaSolaFactura = PlazosExigenUnaSolaFactura(pedido?.plazosPago, pedido?.periodoFacturacion, plazosSonDeLaFicha);
            List<ModoFacturacionPermitidoDTO> modos = Calcular(unaSolaFactura, pedido?.notaEntrega == true);
            List<byte> permitidos = Permitidos(modos);
            byte actual = Constantes.Pedidos.ModosFacturacion.Efectivo(pedido?.modoFacturacion ?? modoAlmacenado, pedido?.mantenerJunto == true);
            byte sugerido = permitidos.Contains(actual) ? actual
                : permitidos.Contains(Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR) ? Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR
                : actual;
            return new Sugerencia
            {
                Modo = sugerido,
                Nombre = Constantes.Pedidos.ModosFacturacion.Nombre(sugerido),
                Motivo = modos.FirstOrDefault(m => m.Modo == actual)?.Motivo,
                ModosPermitidos = permitidos,
                Modos = modos
            };
        }

        /// <summary>Para el POST/PUT: null si el modo se puede guardar, o el motivo por el que no.</summary>
        public static string MotivoRechazo(byte modo, string plazosPago, string periodoFacturacion, bool plazosSonDeLaFicha, bool notaEntrega)
        {
            bool unaSolaFactura = PlazosExigenUnaSolaFactura(plazosPago, periodoFacturacion, plazosSonDeLaFicha);
            return Calcular(unaSolaFactura, notaEntrega).FirstOrDefault(m => m.Modo == modo)?.Motivo;
        }
    }
}
