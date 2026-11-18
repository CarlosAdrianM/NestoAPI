using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Sugerencia 1340: una línea pendiente de servir de lo que el cliente ya tiene (producto en Algete, sin picking).</summary>
    public class LineaPendienteDelCliente
    {
        public string Producto { get; set; }
        public string Texto { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaEntrega { get; set; }
    }

    /// <summary>
    /// Sugerencia 1340: un pedido (o nota de entrega) del cliente con algo pendiente de servir, tal como lo lee
    /// <see cref="IRepositorioJuntoConPendiente"/>. La regla (<see cref="ReglaJuntoConPendiente"/>) decide si cuenta.
    /// </summary>
    public class PendienteDelCliente
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        /// <summary>El contacto de la cabecera (la dirección de entrega).</summary>
        public string Contacto { get; set; }
        public bool EsNotaEntrega { get; set; }
        public byte? ModoServicio { get; set; }
        public bool ServirJunto { get; set; }
        /// <summary>Base imponible de las líneas de producto pendientes (para sumar en los portes, decisión 7).</summary>
        public decimal BasePendiente { get; set; }
        /// <summary>Tiene ya una línea de portes viva (presupuesto, pendiente o en curso): los portes del envío conjunto ya se cobran ahí.</summary>
        public bool LlevaPortes { get; set; }
        /// <summary>Líneas de producto en Algete, pendientes o en curso, con cantidad y SIN picking.</summary>
        public List<LineaPendienteDelCliente> Lineas { get; set; } = new List<LineaPendienteDelCliente>();
    }

    /// <summary>
    /// Sugerencia 1340 (Paloma, tienda de Alcobendas; decisiones de Carlos 18/11/26): el modo de servicio 6, «Junto con lo pendiente del
    /// cliente» (<see cref="Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE"/>). LA REGLA de con qué se junta un pedido, pura:
    /// <list type="bullet">
    /// <item>Lo pendiente del MISMO cliente y contacto (la misma dirección de entrega) y la misma empresa: notas de entrega y pedidos
    /// normales con alguna línea de producto en Algete pendiente o en curso, con cantidad y SIN picking (lo que ya tiene picking está
    /// saliendo: no se espera; el packing suma el segundo envío al que está en curso del mismo cliente y dirección).</item>
    /// <item>NO cuentan: el propio pedido, los presupuestos (sus líneas son -3), los cursos y eventos (sus líneas van en la tienda del
    /// evento, nunca en Algete), las notas de cobro (sin líneas) y de recogida (solo líneas de texto), los retenidos (modo 5) y los otros
    /// pedidos en modo 6: dos pedidos en modo 6 no se esperan el uno al otro; cada uno espera a lo demás y, si no hay nada más, sale
    /// como «Todo junto».</item>
    /// </list>
    /// </summary>
    public static class ReglaJuntoConPendiente
    {
        /// <summary>A los 15 días en modo 6 sin salir se avisa a quien hizo el pedido y a su vendedor (decisión 5).</summary>
        public const int DIAS_AVISO_ESPERA = 15;

        public const string MOTIVO_NADA_PENDIENTE = "El cliente no tiene nada pendiente para esta dirección de entrega con lo que juntar el pedido: " +
            "elija otro modo de servicio (por ejemplo, «Todo junto»).";
        public const string MOTIVO_SIN_SCRIPT = "El modo «Junto con lo pendiente del cliente» todavía no está disponible (falta preparar la base de datos). " +
            "Elija otro modo de servicio.";
        public const string EXPLICACION_NADA_PENDIENTE = "Ya no queda nada pendiente del cliente para esta dirección: saldrá como «Todo junto».";

        /// <summary>¿Cuenta este pendiente para juntar el pedido <paramref name="pedido"/> de ese cliente y contacto?</summary>
        public static bool Cuenta(PendienteDelCliente pendiente, string empresa, int pedido, string cliente, string contacto)
        {
            if (pendiente == null || pendiente.Pedido == pedido)
            {
                return false;
            }
            if (!Igual(pendiente.Empresa, empresa) || !Igual(pendiente.Cliente, cliente) || !Igual(pendiente.Contacto, contacto))
            {
                return false;
            }
            byte modo = Constantes.Pedidos.ModosServicio.Efectivo(pendiente.ModoServicio, pendiente.ServirJunto);
            if (modo == Constantes.Pedidos.ModosServicio.RETENIDO || modo == Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE)
            {
                return false;
            }
            return (pendiente.Lineas ?? new List<LineaPendienteDelCliente>()).Any(l => l != null && l.Cantidad > 0);
        }

        /// <summary>Con lo que se junta, sin repetir y por número.</summary>
        public static List<PendienteDelCliente> ConLoQueSeJunta(IEnumerable<PendienteDelCliente> pendientes, string empresa, int pedido,
            string cliente, string contacto)
        {
            return (pendientes ?? Enumerable.Empty<PendienteDelCliente>())
                .Where(p => Cuenta(p, empresa, pedido, cliente, contacto))
                .GroupBy(p => p.Pedido)
                .Select(g => g.First())
                .OrderBy(p => p.Pedido)
                .ToList();
        }

        /// <summary>«la nota de entrega 929701» o «el pedido 931020».</summary>
        public static string Documento(PendienteDelCliente pendiente)
            => pendiente == null ? string.Empty : (pendiente.EsNotaEntrega ? "la nota de entrega " : "el pedido ") + pendiente.Pedido;

        /// <summary>«la nota de entrega 929701», «la nota de entrega 929701 y el pedido 931020», «…, … y …».</summary>
        public static string Documentos(IEnumerable<PendienteDelCliente> pendientes)
        {
            List<string> partes = (pendientes ?? Enumerable.Empty<PendienteDelCliente>()).Where(p => p != null).Select(Documento).ToList();
            if (partes.Count <= 1)
            {
                return partes.FirstOrDefault() ?? string.Empty;
            }
            return string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes.Last();
        }

        /// <summary>La ayuda del selector: «Saldrá junto con la nota de entrega 929701.»; sin nada, <see cref="EXPLICACION_NADA_PENDIENTE"/>.</summary>
        public static string Explicacion(IEnumerable<PendienteDelCliente> conLoQueSeJunta)
        {
            List<PendienteDelCliente> lista = (conLoQueSeJunta ?? Enumerable.Empty<PendienteDelCliente>()).Where(p => p != null).ToList();
            return lista.Any() ? $"Saldrá junto con {Documentos(lista)}." : EXPLICACION_NADA_PENDIENTE;
        }

        /// <summary>
        /// Decisión 2: la opción del modo 6 para el selector, o null si NO se ofrece. Se ofrece si el contacto tiene algo con lo que
        /// juntarse o si el pedido ya está en modo 6 (para poder enseñarlo y guardarlo sin perderlo; entonces sin nada, con la explicación
        /// de que sale como «Todo junto»). Nunca en un pedido de tienda: allí el cliente se lleva lo que hay.
        /// </summary>
        public static ModoServicioPermitidoDTO Opcion(IEnumerable<PendienteDelCliente> conLoQueSeJunta, bool yaEstaEnModo6,
            ModosServicioPermitidos.TipoAlmacen tipoAlmacen)
        {
            List<PendienteDelCliente> lista = (conLoQueSeJunta ?? Enumerable.Empty<PendienteDelCliente>()).Where(p => p != null).ToList();
            if (tipoAlmacen == ModosServicioPermitidos.TipoAlmacen.Tienda || (!lista.Any() && !yaEstaEnModo6))
            {
                return null;
            }
            return new ModoServicioPermitidoDTO
            {
                Modo = Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(Constantes.Pedidos.ModosServicio.JUNTO_CON_LO_PENDIENTE),
                Permitido = true,
                Explicacion = Explicacion(lista),
                PedidosConLosQueSeJunta = lista.Select(p => p.Pedido).ToList()
            };
        }

        /// <summary>
        /// Una COPIA de la sugerencia con el modo 6 añadido si se ofrece (la original puede venir de la caché por huella del pedido, que
        /// no sabe nada de lo pendiente del cliente). La sugerencia nunca propone el 6: el modo sugerido no cambia.
        /// </summary>
        public static SugeridorModoServicio.Sugerencia ConModoJunto(SugeridorModoServicio.Sugerencia sugerencia, ModoServicioPermitidoDTO opcion)
        {
            if (sugerencia == null || opcion == null)
            {
                return sugerencia;
            }
            var copia = new SugeridorModoServicio.Sugerencia
            {
                Modo = sugerencia.Modo,
                Nombre = sugerencia.Nombre,
                LineasVerdes = sugerencia.LineasVerdes,
                LineasRosas = sugerencia.LineasRosas,
                LineasRojas = sugerencia.LineasRojas,
                HayAlgoQueServirAhora = sugerencia.HayAlgoQueServirAhora,
                Motivo = sugerencia.Motivo,
                ModosPermitidos = (sugerencia.ModosPermitidos ?? new List<byte>()).Where(m => m != opcion.Modo).ToList(),
                Modos = (sugerencia.Modos ?? new List<ModoServicioPermitidoDTO>()).Where(m => m != null && m.Modo != opcion.Modo).ToList()
            };
            // Con lista de permitidos vacía el cliente lo permite todo: añadir el 6 a una lista vacía la restringiría a él solo.
            if (copia.ModosPermitidos.Any())
            {
                copia.ModosPermitidos.Add(opcion.Modo);
            }
            copia.Modos.Add(opcion);
            return copia;
        }

        /// <summary>
        /// Decisión 2, al GUARDAR: por qué no se puede pasar el pedido al modo 6, o null si se puede. Solo se mira al ENTRAR en el 6 (el
        /// cliente que devuelve el 6 que leyó no se rechaza aunque ya no quede nada: el pedido sale entonces como «Todo junto», decisión 4).
        /// </summary>
        public static string MotivoNoSePuedeElegir(bool modoAdmitidoPorLaBd, int pendientesConLosQueJuntarse)
        {
            if (!modoAdmitidoPorLaBd)
            {
                return MOTIVO_SIN_SCRIPT;
            }
            return pendientesConLosQueJuntarse > 0 ? null : MOTIVO_NADA_PENDIENTE;
        }

        /// <summary>Decisión 7: la base imponible de lo pendiente que viaja junto (se suma a la del pedido para el mínimo de portes).</summary>
        public static decimal BaseDeLoPendiente(IEnumerable<PendienteDelCliente> conLoQueSeJunta)
            => (conLoQueSeJunta ?? Enumerable.Empty<PendienteDelCliente>()).Where(p => p != null).Sum(p => p.BasePendiente);

        /// <summary>Decisión 7: si algo de lo pendiente ya lleva portes, el envío conjunto ya los paga ahí (una vez).</summary>
        public static bool LoPendienteYaLlevaPortes(IEnumerable<PendienteDelCliente> conLoQueSeJunta)
            => (conLoQueSeJunta ?? Enumerable.Empty<PendienteDelCliente>()).Any(p => p != null && p.LlevaPortes);

        private static bool Igual(string a, string b)
            => string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
