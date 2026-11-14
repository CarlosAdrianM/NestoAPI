using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857 (fase 3): lo que se sabe de un envío juntando sus correos (<see cref="MismoEnvio.Decidir"/>).</summary>
    public class DecisionEnvio
    {
        /// <summary>Los correos del envío (sin las salidas nuestras).</summary>
        public List<CorreoSeguimientoFila> Correos { get; set; } = new List<CorreoSeguimientoFila>();
        /// <summary>Los candidatos del envío (con motivo <see cref="AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO"/>, para los que no tienen propios).</summary>
        public List<CandidatoPedidoCompra> Candidatos { get; set; } = new List<CandidatoPedidoCompra>();
        public string ProveedorPropuesto { get; set; }
        /// <summary>La entrega estimada más tardía de sus correos.</summary>
        public DateTime? EntregaEstimadaDia { get; set; }
        /// <summary>Un correo del envío ya aplicado a un pedido (el último): ese pedido es el del envío.</summary>
        public CorreoSeguimientoFila Aplicado { get; set; }
        /// <summary>Alguno se ha descartado y ninguno está aplicado.</summary>
        public bool Descartado { get; set; }
        /// <summary>Ya se avisó a Compras por alguno de sus correos.</summary>
        public bool Avisado { get; set; }
        /// <summary>
        /// El primer correo del envío con medición (PedidoReal): un candidato suyo ya tiene albarán desde ese día, así que el envío ha
        /// llegado y ya no hay fecha que poner. Caso real del 13/11/26 (Weelko): recibido el 220873, el job tomó por inequívoco el otro
        /// pedido que quedaba en el aire (220181) y le adelantó la fecha del 23/11 al 16/11 con los correos de un envío que no era suyo.
        /// </summary>
        public CorreoSeguimientoFila Llegado { get; set; }
        public bool YaLlego => Llegado != null;

        /// <summary>El pedido si es inequívoco (un solo candidato), o null.</summary>
        public CandidatoPedidoCompra Unico => ReglaAsignacionInequivoca.Pedido(false, Candidatos);
    }

    /// <summary>
    /// NestoAPI#857 (fase 3, caso real 12/11/26: tres avisos de UPS, uno por bulto, y el de MBE con el proveedor y los tres códigos):
    /// dos correos son del MISMO ENVÍO si comparten algún código de seguimiento (todos los de Seguimientos, no solo el primero) y se
    /// recibieron con menos de <see cref="DIAS"/> días de diferencia. Se agrupan por cierre transitivo. No se guarda ninguna clave: el
    /// envío se calcula cada vez de los códigos. PURA. Las salidas nuestras nunca se enlazan.
    /// </summary>
    public static class MismoEnvio
    {
        public const int DIAS = 10;

        /// <summary>Todos los códigos de un correo, en mayúsculas y sin espacios.</summary>
        public static HashSet<string> Codigos(CorreoSeguimientoFila fila)
        {
            var codigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fila == null)
            {
                return codigos;
            }
            Anadir(codigos, fila.Codigo);
            if (!string.IsNullOrWhiteSpace(fila.Seguimientos))
            {
                try
                {
                    foreach (SeguimientoCorreo s in JsonConvert.DeserializeObject<List<SeguimientoCorreo>>(fila.Seguimientos) ?? new List<SeguimientoCorreo>())
                    {
                        Anadir(codigos, s?.Codigo);
                    }
                }
                catch (JsonException)
                {
                    // Sin los demás: el primero (Codigo) vale
                }
            }
            return codigos;
        }

        private static void Anadir(HashSet<string> codigos, string codigo)
        {
            string limpio = codigo?.Replace(" ", string.Empty).Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(limpio) && AgenciaEntradaPorPatrones.EsCodigo(limpio))
            {
                _ = codigos.Add(limpio);
            }
        }

        public static bool Comparten(CorreoSeguimientoFila a, CorreoSeguimientoFila b) =>
            a != null && b != null && a.EsSalidaNuestra != true && b.EsSalidaNuestra != true
            && Math.Abs((a.Recibido - b.Recibido).TotalDays) < DIAS && Codigos(a).Overlaps(Codigos(b));

        /// <summary>Los correos del envío de <paramref name="fila"/> (ella incluida), de entre <paramref name="todos"/>.</summary>
        public static List<CorreoSeguimientoFila> Grupo(CorreoSeguimientoFila fila, IEnumerable<CorreoSeguimientoFila> todos)
        {
            if (fila == null)
            {
                return new List<CorreoSeguimientoFila>();
            }
            List<CorreoSeguimientoFila> lista = (todos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(f => f != null && f.EsSalidaNuestra != true && (fila.Id <= 0 || f.Id != fila.Id)).ToList();
            var codigos = lista.ToDictionary(f => f, Codigos);
            var grupo = new List<CorreoSeguimientoFila> { fila };
            var porCodigos = new Dictionary<CorreoSeguimientoFila, HashSet<string>> { [fila] = Codigos(fila) };
            for (int i = 0; i < grupo.Count; i++)
            {
                CorreoSeguimientoFila actual = grupo[i];
                foreach (CorreoSeguimientoFila otro in lista.Where(o => !grupo.Contains(o)).ToList())
                {
                    if (Math.Abs((actual.Recibido - otro.Recibido).TotalDays) < DIAS && porCodigos[actual].Overlaps(codigos[otro]))
                    {
                        grupo.Add(otro);
                        porCodigos[otro] = codigos[otro];
                    }
                }
            }
            return grupo.OrderBy(f => f.Recibido).ThenBy(f => f.Id).ToList();
        }

        /// <summary>Todos los envíos (sin las salidas nuestras), cada uno con sus correos por orden de llegada.</summary>
        public static List<List<CorreoSeguimientoFila>> Agrupar(IEnumerable<CorreoSeguimientoFila> todos)
        {
            List<CorreoSeguimientoFila> lista = (todos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(f => f != null && f.EsSalidaNuestra != true).OrderBy(f => f.Recibido).ThenBy(f => f.Id).ToList();
            var vistos = new HashSet<CorreoSeguimientoFila>();
            var grupos = new List<List<CorreoSeguimientoFila>>();
            foreach (CorreoSeguimientoFila fila in lista)
            {
                if (vistos.Contains(fila))
                {
                    continue;
                }
                List<CorreoSeguimientoFila> grupo = Grupo(fila, lista);
                foreach (CorreoSeguimientoFila f in grupo)
                {
                    _ = vistos.Add(f);
                }
                grupos.Add(grupo);
            }
            return grupos;
        }

        /// <summary>Los candidatos propios de un correo (no los heredados de otro del envío).</summary>
        public static List<CandidatoPedidoCompra> Propios(CorreoSeguimientoFila fila) =>
            AsignadorPedidosCompra.Deserializar(fila?.PedidosCandidatos)
                .Where(c => c.Motivo != AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO).ToList();

        /// <summary>
        /// Lo que dice el envío entero: si un correo ya está aplicado, su pedido; si no, los candidatos propios más concretos de sus
        /// correos (la lista más corta que no esté vacía: el número de pedido antes que todos los del proveedor). La entrega estimada,
        /// la más tardía.
        /// </summary>
        public static DecisionEnvio Decidir(IEnumerable<CorreoSeguimientoFila> grupo)
        {
            var decision = new DecisionEnvio
            {
                Correos = (grupo ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null && f.EsSalidaNuestra != true).ToList()
            };
            decision.EntregaEstimadaDia = decision.Correos.Where(f => f.EntregaEstimadaDia.HasValue).Select(f => (DateTime?)f.EntregaEstimadaDia.Value.Date).Max();
            decision.Avisado = decision.Correos.Any(f => f.AvisadoPorAsignar.HasValue);
            // También con el envío ya aplicado: llegado el pedido, los correos que vengan después no vuelven a poner fecha
            decision.Llegado = decision.Correos.Where(f => f.PedidoReal.HasValue).OrderBy(f => f.Recibido).ThenBy(f => f.Id).FirstOrDefault();
            decision.Aplicado = decision.Correos.Where(f => f.PedidoAplicado.HasValue)
                .OrderByDescending(f => f.AplicadoEl ?? DateTime.MinValue).ThenByDescending(f => f.Id).FirstOrDefault();
            if (decision.Aplicado != null)
            {
                CorreoSeguimientoFila a = decision.Aplicado;
                string proveedor = AsignadorPedidosCompra.Deserializar(a.PedidosCandidatos)
                    .FirstOrDefault(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido) == AsignadorPedidosCompra.Clave(a.EmpresaAplicada, a.PedidoAplicado.Value))
                    ?.Proveedor ?? a.ProveedorPropuesto;
                decision.Candidatos.Add(new CandidatoPedidoCompra
                {
                    Empresa = a.EmpresaAplicada?.Trim(), Pedido = a.PedidoAplicado.Value, Proveedor = proveedor, Motivo = AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO
                });
                decision.ProveedorPropuesto = proveedor;
                return decision;
            }
            decision.Descartado = decision.Correos.Any(f => f.Descartado == true);
            List<CandidatoPedidoCompra> propios = decision.Correos.Select(Propios).Where(l => l.Any())
                .OrderBy(l => l.Count).FirstOrDefault() ?? new List<CandidatoPedidoCompra>();
            decision.Candidatos = propios.Select(c => new CandidatoPedidoCompra
            {
                Empresa = c.Empresa, Pedido = c.Pedido, Proveedor = c.Proveedor, Motivo = AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO
            }).ToList();
            List<string> proveedores = decision.Candidatos.Select(c => c.Proveedor?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            decision.ProveedorPropuesto = proveedores.Count == 1 ? proveedores[0] : null;
            return decision;
        }

        /// <summary>¿Hay que ponerle a este correo los candidatos del envío? Si no tiene propios (o si el envío ya está aplicado).</summary>
        public static bool Hereda(CorreoSeguimientoFila fila, DecisionEnvio decision) =>
            fila != null && decision != null && !fila.PedidoAplicado.HasValue && fila.Descartado != true && decision.Candidatos.Any()
            && (decision.Aplicado != null || !Propios(fila).Any());
    }

    /// <summary>
    /// NestoAPI#857 (fase 3, Carlos 12/11/26): con varios candidatos NO se elige solo (decide Compras), pero se sugiere UNO: el enviado
    /// más recientemente ANTES de que llegara el primer correo del envío (un pedido enviado después no puede ser lo que ya viene). PURA.
    /// </summary>
    public static class ReglaPedidoSugerido
    {
        public static PedidoCompraEnAire Sugerido(IEnumerable<PedidoCompraEnAire> candidatos, DateTime primerCorreo) =>
            (candidatos ?? Enumerable.Empty<PedidoCompraEnAire>())
                .Where(p => p?.FechaEnvio != null && p.FechaEnvio.Value < primerCorreo)
                .OrderByDescending(p => p.FechaEnvio.Value).ThenByDescending(p => p.Pedido)
                .FirstOrDefault();

        /// <summary>De los pedidos en el aire, los que son candidatos del envío.</summary>
        public static List<PedidoCompraEnAire> DelEnvio(IEnumerable<PedidoCompraEnAire> enAire, IEnumerable<CandidatoPedidoCompra> candidatos)
        {
            var claves = new HashSet<string>((candidatos ?? Enumerable.Empty<CandidatoPedidoCompra>()).Select(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido)));
            return (enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null && claves.Contains(AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido))).ToList();
        }
    }
}
