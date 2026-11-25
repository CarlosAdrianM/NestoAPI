using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857i: una fila de dbo.ComprasOrigenesAgencia (agencia + delegación de origen → proveedor, aprendida).</summary>
    public class OrigenAgenciaRecordado
    {
        public int Id { get; set; }
        /// <summary>El nombre de la agencia en la API («DB Schenker»).</summary>
        public string Agencia { get; set; }
        /// <summary>La clave corta del origen que saca la clase de la agencia («VIC»).</summary>
        public string Origen { get; set; }
        /// <summary>Solo al leer: el nombre del origen si la agencia lo sabe («Vic»); si no, null.</summary>
        public string NombreOrigen { get; set; }
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        /// <summary>Solo al leer: Proveedores.Nombre.</summary>
        public string NombreProveedor { get; set; }
        public int Veces { get; set; }
        public DateTime UltimaVez { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#857i: el origen de un envío según su agencia.</summary>
    public class OrigenEnvioAgencia
    {
        public string Agencia { get; set; }
        public string Origen { get; set; }
        /// <summary>El nombre que sabe la agencia («Vic») o, si no, la clave.</summary>
        public string Nombre { get; set; }

        internal string Clave => (Agencia?.Trim() ?? string.Empty).ToUpperInvariant() + "|" + (Origen?.Trim() ?? string.Empty).ToUpperInvariant();
    }

    /// <summary>
    /// NestoAPI#857i: los orígenes de los códigos de un correo, preguntando a la clase de SU agencia (<see cref="IAgenciaEntrada.OrigenDelEnvio"/>);
    /// fuera de ella no se sabe nada del formato de su código. Un código de otro mensajero (<see cref="SeguimientoCorreo.Agencia"/>) se pregunta
    /// a esa agencia.
    /// </summary>
    public static class OrigenesAgenciaCorreo
    {
        public static List<OrigenEnvioAgencia> De(string agenciaCorreo, IEnumerable<SeguimientoCorreo> seguimientos, IEnumerable<IAgenciaEntrada> agencias = null)
        {
            List<IAgenciaEntrada> registradas = (agencias ?? AgenciasEntradaRegistradas.Todas).Where(a => a != null).ToList();
            var resultado = new List<OrigenEnvioAgencia>();
            foreach (SeguimientoCorreo s in (seguimientos ?? Enumerable.Empty<SeguimientoCorreo>()).Where(s => s != null && !string.IsNullOrWhiteSpace(s.Codigo)))
            {
                string nombreAgencia = !string.IsNullOrWhiteSpace(s.Agencia) ? s.Agencia : agenciaCorreo;
                IAgenciaEntrada agencia = string.IsNullOrWhiteSpace(nombreAgencia) ? null
                    : registradas.FirstOrDefault(a => string.Equals(a.Nombre, nombreAgencia.Trim(), StringComparison.OrdinalIgnoreCase));
                string origen = agencia?.OrigenDelEnvio(s.Codigo);
                if (string.IsNullOrWhiteSpace(origen))
                {
                    continue;
                }
                var o = new OrigenEnvioAgencia { Agencia = agencia.Nombre, Origen = origen.Trim().ToUpperInvariant() };
                o.Nombre = agencia.NombreOrigen(o.Origen) ?? o.Origen;
                if (!resultado.Any(r => r.Clave == o.Clave))
                {
                    resultado.Add(o);
                }
            }
            return resultado;
        }

        /// <summary>Los de un correo guardado (Codigo y Seguimientos).</summary>
        public static List<OrigenEnvioAgencia> De(CorreoSeguimientoFila fila, IEnumerable<IAgenciaEntrada> agencias = null)
        {
            if (fila == null)
            {
                return new List<OrigenEnvioAgencia>();
            }
            var seguimientos = new List<SeguimientoCorreo>();
            if (!string.IsNullOrWhiteSpace(fila.Codigo))
            {
                seguimientos.Add(new SeguimientoCorreo { Codigo = fila.Codigo });
            }
            if (!string.IsNullOrWhiteSpace(fila.Seguimientos))
            {
                try
                {
                    seguimientos.AddRange(JsonConvert.DeserializeObject<List<SeguimientoCorreo>>(fila.Seguimientos) ?? new List<SeguimientoCorreo>());
                }
                catch (JsonException)
                {
                    // Con el primero (Codigo) vale
                }
            }
            return De(fila.Agencia, seguimientos, agencias);
        }

        /// <summary>Los de todos los correos de un envío, sin repetir.</summary>
        public static List<OrigenEnvioAgencia> DelEnvio(IEnumerable<CorreoSeguimientoFila> correos, IEnumerable<IAgenciaEntrada> agencias = null)
        {
            var resultado = new List<OrigenEnvioAgencia>();
            foreach (OrigenEnvioAgencia o in (correos ?? Enumerable.Empty<CorreoSeguimientoFila>()).SelectMany(f => De(f, agencias)))
            {
                if (!resultado.Any(r => r.Clave == o.Clave))
                {
                    resultado.Add(o);
                }
            }
            return resultado;
        }

        /// <summary>Lo que se enseña: «Vic» (o la clave), varios separados por coma; null si no hay.</summary>
        public static string Texto(IEnumerable<OrigenEnvioAgencia> origenes)
        {
            List<string> nombres = (origenes ?? Enumerable.Empty<OrigenEnvioAgencia>()).Select(o => o.Nombre).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            return nombres.Any() ? string.Join(", ", nombres) : null;
        }
    }

    /// <summary>
    /// NestoAPI#857i (regla de Carlos 25/11/26, caso DB Schenker ESVIC262013788: un aviso sin remitente, pedido ni referencia). Cuando NINGÚN
    /// otro criterio ha dado proveedor (va detrás del nombre del remitente del paquete) y el código dice de qué delegación sale el envío, se
    /// mira qué proveedores han mandado antes desde allí por esa agencia (dbo.ComprasOrigenesAgencia, últimos <see cref="MESES"/> meses):
    /// <list type="bullet">
    /// <item>Ninguno → como antes (nada).</item>
    /// <item>UNO, con UN pedido en el aire enviado antes del correo → ese (inequívoco, <see cref="AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA"/>).</item>
    /// <item>UNO con varios → sus pedidos, por asignar (si tiene control de pendientes, decide después <see cref="ReglaControlPendientes"/>).</item>
    /// <item>DOS O MÁS → nunca por frecuencia: si solo uno tiene pedidos enviados antes del correo y es UNO, ese; si no, por asignar con los
    /// pedidos de esos proveedores (<see cref="AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA_VARIOS"/>, que no deja decidir a otra regla).</item>
    /// </list>
    /// Se aprende de lo que asigna una PERSONA o, en el job, un criterio fuerte (<see cref="MotivosQueEnsenan"/>); nunca de lo que asigna esta
    /// misma regla. PURA.
    /// </summary>
    public static class ReglaOrigenAgencia
    {
        public const int MESES = 12;

        /// <summary>Los criterios del job de los que se aprende (nuestro número, el PDF del aviso y las líneas del aviso).</summary>
        internal static readonly HashSet<string> MotivosQueEnsenan = new HashSet<string>
        {
            AsignadorPedidosCompra.MOTIVO_NUMERO, AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO, AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO
        };

        /// <summary>
        /// La asignación por el origen, o null si no toca (sin origen, nada recordado o ninguno con pedidos en el aire enviados antes del correo).
        /// </summary>
        public static AsignacionPedidoCompra Asignar(IEnumerable<OrigenEnvioAgencia> origenes, IEnumerable<OrigenAgenciaRecordado> recordados,
            IEnumerable<PedidoCompraEnAire> pedidos, DateTime recibido)
        {
            List<OrigenEnvioAgencia> delEnvio = (origenes ?? Enumerable.Empty<OrigenEnvioAgencia>()).Where(o => o != null && !string.IsNullOrWhiteSpace(o.Origen)).ToList();
            if (!delEnvio.Any() || recordados == null)
            {
                return null;
            }
            var claves = new HashSet<string>(delEnvio.Select(o => o.Clave));
            DateTime desde = recibido.AddMonths(-MESES);
            var proveedores = recordados
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Proveedor) && r.UltimaVez >= desde
                    && claves.Contains(new OrigenEnvioAgencia { Agencia = r.Agencia, Origen = r.Origen }.Clave))
                .GroupBy(r => AsignadorPedidosCompra.Clave(r.Empresa, 0) + (r.Proveedor.Trim().ToUpperInvariant()))
                .Select(g => g.OrderByDescending(r => r.UltimaVez).First())
                .ToList();
            if (!proveedores.Any())
            {
                return null;
            }
            List<PedidoCompraEnAire> enAire = (pedidos ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null).ToList();
            var conPedidos = proveedores
                .Select(r => new
                {
                    Recordado = r,
                    Pedidos = enAire.Where(p => string.Equals(p.Proveedor?.Trim(), r.Proveedor.Trim(), StringComparison.OrdinalIgnoreCase)
                            && string.Equals(p.Empresa?.Trim(), r.Empresa?.Trim(), StringComparison.OrdinalIgnoreCase)
                            && ReglaControlPendientes.FechaEnvio(p).HasValue && ReglaControlPendientes.FechaEnvio(p).Value < recibido)
                        .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).Select(g => g.First())
                        .OrderBy(p => p.Pedido).ToList()
                })
                .ToList();
            OrigenEnvioAgencia origen = delEnvio[0];
            string agencia = origen.Agencia;
            string lugar = origen.Nombre ?? origen.Origen;
            var conAlgo = conPedidos.Where(c => c.Pedidos.Any()).ToList();
            if (!conAlgo.Any())
            {
                return null;
            }

            if (proveedores.Count == 1)
            {
                var unico = conAlgo[0];
                string nombre = Nombre(unico.Recordado, unico.Pedidos);
                if (unico.Pedidos.Count == 1)
                {
                    return Componer(unico.Pedidos, AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA,
                        $"{agencia} desde {lugar}: las otras veces era de {nombre}; se asigna a su único pedido en el aire, {unico.Pedidos[0].Pedido}.");
                }
                return Componer(unico.Pedidos, AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA,
                    $"{agencia} desde {lugar}: las otras veces era de {nombre}, que tiene {unico.Pedidos.Count} pedidos en el aire ({Lista(unico.Pedidos.Select(p => p.Pedido.ToString(CultureInfo.InvariantCulture)))}): elija el pedido.");
            }

            string todos = Lista(conPedidos.Select(c => Nombre(c.Recordado, c.Pedidos)));
            if (conAlgo.Count == 1 && conAlgo[0].Pedidos.Count == 1)
            {
                var elegido = conAlgo[0];
                List<string> sinNada = conPedidos.Where(c => !c.Pedidos.Any()).Select(c => Nombre(c.Recordado, c.Pedidos)).ToList();
                string otros = sinNada.Count == 1 ? $"{sinNada[0]} no tiene nada pendiente" : $"{Lista(sinNada)} no tienen nada pendiente";
                return Componer(elegido.Pedidos, AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA,
                    $"Desde {lugar} nos mandan por {agencia} {todos}; {otros}: se asigna al único pedido en el aire de {Nombre(elegido.Recordado, elegido.Pedidos)}, {elegido.Pedidos[0].Pedido}.");
            }
            return Componer(conAlgo.SelectMany(c => c.Pedidos).ToList(), AsignadorPedidosCompra.MOTIVO_ORIGEN_AGENCIA_VARIOS,
                $"Desde {lugar} nos mandan por {agencia} {todos}: elija el pedido.");
        }

        /// <summary>
        /// La asignación de siempre si ya tiene candidatos (otro criterio manda); si no, la del origen (o la misma, vacía, si no toca).
        /// </summary>
        public static AsignacionPedidoCompra Completar(AsignacionPedidoCompra asignacion, IEnumerable<OrigenEnvioAgencia> origenes,
            IEnumerable<OrigenAgenciaRecordado> recordados, IEnumerable<PedidoCompraEnAire> pedidos, DateTime recibido)
        {
            if (asignacion?.Candidatos != null && asignacion.Candidatos.Any())
            {
                return asignacion;
            }
            return Asignar(origenes, recordados, pedidos, recibido) ?? asignacion;
        }

        /// <summary>
        /// En el job: ¿el envío aplicado a <paramref name="pedido"/> enseña? Solo si algún correo suyo tenía ese pedido por un criterio fuerte
        /// (<see cref="MotivosQueEnsenan"/>); lo que salió por esta misma regla (o por el control de pendientes sobre sus candidatos) no.
        /// </summary>
        public static bool EnsenaAutomatico(IEnumerable<CorreoSeguimientoFila> correos, CandidatoPedidoCompra pedido)
        {
            if (pedido == null || string.IsNullOrWhiteSpace(pedido.Proveedor))
            {
                return false;
            }
            string clave = AsignadorPedidosCompra.Clave(pedido.Empresa, pedido.Pedido);
            return (correos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(f => f != null)
                .SelectMany(MismoEnvio.Propios)
                .Any(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido) == clave && MotivosQueEnsenan.Contains(c.Motivo ?? string.Empty));
        }

        private static string Nombre(OrigenAgenciaRecordado recordado, List<PedidoCompraEnAire> pedidos)
        {
            string nombre = pedidos?.Select(p => p.NombreProveedor).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? recordado.NombreProveedor;
            return string.IsNullOrWhiteSpace(nombre) ? "el proveedor " + recordado.Proveedor.Trim() : nombre.Trim();
        }

        private static string Lista(IEnumerable<string> textos)
        {
            List<string> lista = textos.ToList();
            return lista.Count <= 1 ? lista.FirstOrDefault() ?? string.Empty : string.Join(", ", lista.Take(lista.Count - 1)) + " y " + lista.Last();
        }

        private static AsignacionPedidoCompra Componer(List<PedidoCompraEnAire> pedidos, string motivo, string explicacion)
        {
            List<string> distintos = pedidos.Select(p => p.Proveedor?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new AsignacionPedidoCompra
            {
                ProveedorPropuesto = distintos.Count == 1 ? distintos[0] : null,
                Candidatos = pedidos
                    .OrderByDescending(p => p.FechaEnvio ?? DateTime.MinValue).ThenByDescending(p => p.Pedido)
                    .Take(AsignadorPedidosCompra.MAXIMO_CANDIDATOS)
                    .Select(p => new CandidatoPedidoCompra
                    {
                        Empresa = p.Empresa?.Trim(), Pedido = p.Pedido, Proveedor = p.Proveedor?.Trim(), Motivo = motivo, Explicacion = explicacion
                    })
                    .ToList()
            };
        }
    }
}
