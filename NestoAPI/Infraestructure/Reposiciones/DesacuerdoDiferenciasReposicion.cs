using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#906 (sugerencia 1433, Santiago; decisiones de Carlos 25/11/26): «En discusión» en Diferencias de reposición.
    // Una FALTA (se recibió de menos) en la que el origen dice que salió y el destino que no llegó. La abre el origen, avisa al
    // destino, que contesta «Lo he encontrado» (= Error al recibir) o «No está». Si los dos mantienen su versión (o vence el
    // plazo), NO decide Dirección («es trabajo»): decide el sistema con una regla fija (ReglaImputacionDesacuerdo), unidad a
    // unidad, y lo deja todo apuntado (rasgos, decisión, paso y, después, la verdad) para entrenar un modelo más adelante.
    // Aquí, solo reglas puras.
    // ------------------------------------------------------------------------------------------------------------------

    public static class EstadosDiscusionDiferencia
    {
        public const string ABIERTA = "Abierta";
        /// <summary>Se pusieron de acuerdo (el destino lo encontró, o el origen resolvió de otra forma).</summary>
        public const string ACORDADA = "Acordada";
        /// <summary>Lo decidió la regla (los dos mantuvieron su versión o venció el plazo).</summary>
        public const string DECIDIDA = "Decidida";
    }

    public static class RespuestasDiscusionDiferencia
    {
        /// <summary>El destino lo ha encontrado: es un error al recibir.</summary>
        public const string LO_HE_ENCONTRADO = "LoHeEncontrado";
        /// <summary>El destino confirma que no está: los dos mantienen su versión.</summary>
        public const string NO_ESTA = "NoEsta";

        public static string Normalizar(string respuesta)
        {
            string limpia = respuesta?.Trim();
            return new[] { LO_HE_ENCONTRADO, NO_ESTA }.FirstOrDefault(r => string.Equals(r, limpia, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class PasosImputacionDesacuerdo
    {
        public const string PRUEBA = "Prueba";
        public const string INDICADOR = "Indicador";
        public const string ALTERNANCIA = "Alternancia";
        public static readonly string[] TODOS = { PRUEBA, INDICADOR, ALTERNANCIA };
    }

    public static class ComoSeDemostroError
    {
        public const string RECONOCIDO = "Reconocido";
        public const string PRUEBA = "Prueba";
        public const string INVENTARIO = "Inventario";
    }

    /// <summary>NestoAPI#906: un bulto de la reposición y si consta que llegó al destino (leído al recibir o entregado por el repartidor).</summary>
    public class BultoDesacuerdo
    {
        public string Codigo { get; set; }
        /// <summary>ReposicionesBultos.FechaLectura: se leyó al recibir.</summary>
        public DateTime? LeidoAlRecibir { get; set; }
        /// <summary>RepartoBultosMovimientos «Entregado» de ese bulto (el repartidor lo dejó en el destino).</summary>
        public DateTime? Entregado { get; set; }
        public bool EnDestino => LeidoAlRecibir.HasValue || Entregado.HasValue;
        public DateTime? Fecha => new[] { Entregado, LeidoAlRecibir }.Where(f => f.HasValue).Min();
    }

    /// <summary>NestoAPI#906: las pruebas que quedan registradas hoy de una reposición y un producto.</summary>
    public class PruebasDesacuerdo
    {
        public int Enviado { get; set; }
        /// <summary>Unidades del producto LEÍDAS (Metodo SCAN, no tecleadas) al preparar la salida (PreparacionEscaneos REPO).</summary>
        public int EscaneadasOrigen { get; set; }
        public List<BultoDesacuerdo> Bultos { get; set; } = new List<BultoDesacuerdo>();

        public int SinLecturaOrigen => Math.Max(0, Enviado - Math.Max(0, EscaneadasOrigen));
        /// <summary>Consta que llegó al destino: la reposición tiene bultos y TODOS se leyeron al recibir o se entregaron (no se sabe en cuál iba).</summary>
        public bool BultosEnDestino => Bultos != null && Bultos.Any() && Bultos.All(b => b.EnDestino);
    }

    /// <summary>NestoAPI#906: el indicador de errores demostrados de un almacén (12 meses).</summary>
    public class IndicadorErroresAlmacen
    {
        public string Almacen { get; set; }
        public int Discrepancias { get; set; }
        public int Errores { get; set; }
        /// <summary>Errores / discrepancias; null sin discrepancias (sin datos).</summary>
        public decimal? Valor => Discrepancias == 0 ? (decimal?)null : Math.Round((decimal)Errores / Discrepancias, 4);
    }

    public class CasoImputacionDesacuerdo
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        public int Unidades { get; set; }
        public PruebasDesacuerdo Pruebas { get; set; } = new PruebasDesacuerdo();
        public IndicadorErroresAlmacen IndicadorOrigen { get; set; }
        public IndicadorErroresAlmacen IndicadorDestino { get; set; }
        /// <summary>A qué almacén fue la última unidad decidida por alternancia entre estos dos (en cualquier sentido); null si ninguna.</summary>
        public string UltimoTurno { get; set; }
    }

    public class ImputacionUnidadDesacuerdo
    {
        public int Unidad { get; set; }
        public string Almacen { get; set; }
        public string Paso { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#906, LA REGLA de imputación (Carlos 25/11/26). Una unidad nunca se parte: va entera a un almacén.
    /// <list type="number">
    /// <item>Manda la prueba: las unidades que el origen NO leyó al enviar (enviadas − leídas por escáner; lo tecleado no es lectura)
    /// van al ORIGEN. Las leídas, si consta que los bultos llegaron al destino (leídos al recibir o entregados por el repartidor),
    /// van al DESTINO.</item>
    /// <item>Si la prueba no lo aclara (leída al enviar, pero no consta que el bulto llegase), va al almacén con PEOR indicador de
    /// errores demostrados en 12 meses.</item>
    /// <item>Mismo indicador o sin datos: se alterna entre los dos (una a cada uno; el turno sale de la última alternancia de la pareja;
    /// la primera, al origen).</item>
    /// </list>
    /// Cuando no se sabe en qué bulto iba la unidad, «llegó» exige que lleguen TODOS los bultos de la reposición.
    /// </summary>
    public static class ReglaImputacionDesacuerdo
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static List<ImputacionUnidadDesacuerdo> Decidir(CasoImputacionDesacuerdo caso)
        {
            var resultado = new List<ImputacionUnidadDesacuerdo>();
            if (caso == null || caso.Unidades <= 0)
            {
                return resultado;
            }
            string origen = caso.Origen?.Trim().ToUpperInvariant();
            string destino = caso.Destino?.Trim().ToUpperInvariant();
            PruebasDesacuerdo pruebas = caso.Pruebas ?? new PruebasDesacuerdo();
            int sinLectura = pruebas.SinLecturaOrigen;
            string turno = caso.UltimoTurno?.Trim().ToUpperInvariant();
            for (int unidad = 1; unidad <= caso.Unidades; unidad++)
            {
                var decision = new ImputacionUnidadDesacuerdo { Unidad = unidad };
                if (unidad <= sinLectura)
                {
                    decision.Almacen = origen;
                    decision.Paso = PasosImputacionDesacuerdo.PRUEBA;
                    decision.Motivo = $"Va a {Nombre(origen)}: {Nombre(origen)} no la leyó al enviar (leyó {Math.Max(0, pruebas.EscaneadasOrigen)} de {pruebas.Enviado}).";
                }
                else if (pruebas.BultosEnDestino)
                {
                    decision.Almacen = destino;
                    decision.Paso = PasosImputacionDesacuerdo.PRUEBA;
                    decision.Motivo = $"Va a {Nombre(destino)}: {Nombre(origen)} la leyó al enviar y {TextoBultos(pruebas.Bultos, destino)}.";
                }
                else
                {
                    string porQue = pruebas.Bultos == null || !pruebas.Bultos.Any()
                        ? $"{Nombre(origen)} la leyó al enviar, pero la reposición no tiene bultos con etiqueta"
                        : $"{Nombre(origen)} la leyó al enviar, pero no consta que todos los bultos llegasen a {Nombre(destino)}";
                    decimal? vo = caso.IndicadorOrigen?.Valor;
                    decimal? vd = caso.IndicadorDestino?.Valor;
                    if (vo.HasValue && vd.HasValue && vo.Value != vd.Value)
                    {
                        bool peorOrigen = vo.Value > vd.Value;
                        decision.Almacen = peorOrigen ? origen : destino;
                        decision.Paso = PasosImputacionDesacuerdo.INDICADOR;
                        decision.Motivo = $"Va a {Nombre(decision.Almacen)}: {porQue}, y {Nombre(decision.Almacen)} tiene peor indicador de errores " +
                            $"demostrados en 12 meses ({Porcentaje(peorOrigen ? vo.Value : vd.Value)} frente a {Porcentaje(peorOrigen ? vd.Value : vo.Value)}).";
                    }
                    else
                    {
                        string anterior = turno;
                        decision.Almacen = anterior == origen ? destino : (anterior == destino ? origen : origen);
                        decision.Paso = PasosImputacionDesacuerdo.ALTERNANCIA;
                        string indicador = vo.HasValue && vd.HasValue ? "con el mismo indicador de errores" : "sin datos del indicador de errores";
                        string turnoTexto = anterior == origen || anterior == destino
                            ? $"la anterior fue a {Nombre(anterior)}"
                            : $"es la primera entre {Nombre(origen)} y {Nombre(destino)}";
                        decision.Motivo = $"Va a {Nombre(decision.Almacen)}: {porQue}, {indicador}; le toca por turno ({turnoTexto}).";
                        turno = decision.Almacen;
                    }
                }
                resultado.Add(decision);
            }
            return resultado;
        }

        private static string TextoBultos(List<BultoDesacuerdo> bultos, string destino)
        {
            List<BultoDesacuerdo> lista = bultos.OrderBy(b => b.Codigo, StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 1)
            {
                BultoDesacuerdo b = lista[0];
                string como = b.Entregado.HasValue ? "se entregó" : "se leyó al recibir";
                return $"el bulto {b.Codigo} {como} en {Nombre(destino)} el {b.Fecha.Value.ToString("dd/MM", es)}";
            }
            DateTime ultimo = lista.Max(b => b.Fecha.Value);
            return $"sus {lista.Count} bultos ({lista.First().Codigo} a {lista.Last().Codigo}) llegaron a {Nombre(destino)} (el último, el {ultimo.ToString("dd/MM", es)})";
        }

        internal static string Porcentaje(decimal valor) => (valor * 100).ToString("0.#", es) + " %";

        internal static string Nombre(string almacen) => ServicioDiferenciasReposicion.NombreAlmacen(almacen);

        /// <summary>«Va a Reina 1 ud. y a Algete 1 ud.»: el resumen de la decisión.</summary>
        public static string Resumen(IEnumerable<ImputacionUnidadDesacuerdo> decisiones)
        {
            return string.Join(" ", (decisiones ?? Enumerable.Empty<ImputacionUnidadDesacuerdo>()).Select(d =>
                $"Ud. {d.Unidad}: {d.Motivo}"));
        }
    }

    /// <summary>
    /// NestoAPI#906: los movimientos de stock de una decisión, por el cauce de las diferencias (diario _ErrRepo, <see cref="ITransaccionDiferenciasReposicion.Mover"/>).
    /// Las unidades «salieron» del origen con el traspaso y no se dieron de alta en el destino; si al recibir se hizo el ajuste,
    /// volvieron a contar en el origen. Al final ninguno de los dos las tiene, y la BAJA queda en el almacén imputado (para el informe
    /// de pérdida desconocida, #893, con el texto «Desacuerdo de reposición N (pérdida)»):
    /// <list type="bullet">
    /// <item>Origen imputado: baja de lo que volvió con el ajuste (si no se ajustó, entra y sale para que conste la baja).</item>
    /// <item>Destino imputado: lo que volvió al origen sale de allí («no vuelve») y en el destino entra («llegó») y sale («pérdida»).</item>
    /// </list>
    /// Puro: el saldo final de cada almacén es el mismo que si nadie las tuviera.
    /// </summary>
    public static class PlanStockDesacuerdo
    {
        public static List<MovimientoStockDiferencia> Movimientos(string empresa, int traspaso, string origen, string destino, string producto,
            bool ajusteHecho, int unidades, int alOrigen, int alDestino, string usuario)
        {
            var movimientos = new List<MovimientoStockDiferencia>();
            int yaAjustado = ajusteHecho ? unidades : 0;
            MovimientoStockDiferencia M(string almacen, int cantidad, string que) => new MovimientoStockDiferencia
            {
                Empresa = empresa,
                Almacen = almacen,
                Producto = producto,
                Cantidad = cantidad,
                Traspaso = traspaso,
                Texto = Texto(traspaso, que),
                Usuario = usuario,
                PrimeroPendienteDeUbicar = true
            };
            if (alOrigen > 0)
            {
                if (yaAjustado == 0)
                {
                    movimientos.Add(M(origen, alOrigen, "vuelve"));
                }
                movimientos.Add(M(origen, -alOrigen, "pérdida"));
            }
            int noVuelve = yaAjustado - (yaAjustado > 0 ? alOrigen : 0);
            if (noVuelve > 0)
            {
                movimientos.Add(M(origen, -noVuelve, "no vuelve"));
            }
            if (alDestino > 0)
            {
                movimientos.Add(M(destino, alDestino, "llegó"));
                movimientos.Add(M(destino, -alDestino, "pérdida"));
            }
            return movimientos;
        }

        /// <summary>«Desacuerdo de reposición 81463 (pérdida)» (cabe en el texto del extracto).</summary>
        public static string Texto(int traspaso, string que) => $"Desacuerdo de reposición {traspaso} ({que})";
    }

    /// <summary>NestoAPI#906: una diferencia de los últimos meses, para el indicador de errores demostrados.</summary>
    public class DiferenciaParaIndicador
    {
        public int Id { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public int Diferencia { get; set; }
        public string Resolucion { get; set; }
        public string Preparador { get; set; }
        public string UsuarioRecepcion { get; set; }
        /// <summary>De las unidades en desacuerdo: los almacenes con error demostrado (decididas por la PRUEBA, o con verdad Reconocido/Inventario).</summary>
        public List<string> ErroresDemostradosDe { get; set; } = new List<string>();
    }

    public class IndicadorErroresUsuario
    {
        public string Usuario { get; set; }
        public int Discrepancias { get; set; }
        public int Errores { get; set; }
        public decimal? Valor => Discrepancias == 0 ? (decimal?)null : Math.Round((decimal)Errores / Discrepancias, 4);
    }

    public class IndicadorErroresDTO
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<IndicadorErroresAlmacen> Almacenes { get; set; } = new List<IndicadorErroresAlmacen>();
        /// <summary>Solo para consulta: el origen se atribuye a quien preparó y el destino a quien recibió.</summary>
        public List<IndicadorErroresUsuario> Usuarios { get; set; } = new List<IndicadorErroresUsuario>();
    }

    /// <summary>
    /// NestoAPI#906: errores DEMOSTRADOS (Carlos 25/11/26) por almacén. Un error es de un almacén solo si (a) lo reconoce —en el
    /// destino «Error al recibir» (también «Lo he encontrado») y «Sobraba» (leyó de más); en el origen «Confirmada» (no salió o salió de
    /// más) y «No estaba»—, (b) lo demuestra la prueba (unidad en desacuerdo decidida por el paso «Prueba») o (c) un inventario posterior
    /// (la verdad del job). Lo decidido por indicador o alternancia NO cuenta. Indicador = errores / discrepancias en que participó.
    /// </summary>
    public static class ReglaIndicadorErrores
    {
        public static List<string> ErroresDe(DiferenciaParaIndicador d)
        {
            var errores = new List<string>();
            string origen = d?.Origen?.Trim().ToUpperInvariant();
            string destino = d?.Destino?.Trim().ToUpperInvariant();
            switch (d?.Resolucion?.Trim())
            {
                case ResolucionesDiferenciaReposicion.ERROR_RECEPCION:
                case ResolucionesDiferenciaReposicion.SOBRABA:
                    errores.Add(destino);
                    break;
                case ResolucionesDiferenciaReposicion.CONFIRMADA:
                case ResolucionesDiferenciaReposicion.NO_ESTABA:
                    errores.Add(origen);
                    break;
            }
            errores.AddRange((d?.ErroresDemostradosDe ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().ToUpperInvariant()));
            return errores.Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
        }

        public static List<IndicadorErroresAlmacen> PorAlmacen(IEnumerable<DiferenciaParaIndicador> diferencias)
        {
            var porAlmacen = new Dictionary<string, IndicadorErroresAlmacen>(StringComparer.OrdinalIgnoreCase);
            foreach (DiferenciaParaIndicador d in (diferencias ?? Enumerable.Empty<DiferenciaParaIndicador>()).Where(x => x != null))
            {
                List<string> errores = ErroresDe(d);
                foreach (string almacen in new[] { d.Origen, d.Destino }.Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim().ToUpperInvariant()).Distinct())
                {
                    if (!porAlmacen.TryGetValue(almacen, out IndicadorErroresAlmacen fila))
                    {
                        porAlmacen[almacen] = fila = new IndicadorErroresAlmacen { Almacen = almacen };
                    }
                    fila.Discrepancias++;
                    if (errores.Contains(almacen))
                    {
                        fila.Errores++;
                    }
                }
            }
            return porAlmacen.Values.OrderBy(a => a.Almacen).ToList();
        }

        public static List<IndicadorErroresUsuario> PorUsuario(IEnumerable<DiferenciaParaIndicador> diferencias)
        {
            var porUsuario = new Dictionary<string, IndicadorErroresUsuario>(StringComparer.OrdinalIgnoreCase);
            void Sumar(string usuario, bool error)
            {
                string limpio = usuario?.Trim();
                if (string.IsNullOrEmpty(limpio))
                {
                    return;
                }
                limpio = limpio.Contains("\\") ? limpio.Substring(limpio.LastIndexOf('\\') + 1) : limpio;
                if (!porUsuario.TryGetValue(limpio, out IndicadorErroresUsuario fila))
                {
                    porUsuario[limpio] = fila = new IndicadorErroresUsuario { Usuario = limpio };
                }
                fila.Discrepancias++;
                if (error)
                {
                    fila.Errores++;
                }
            }
            foreach (DiferenciaParaIndicador d in (diferencias ?? Enumerable.Empty<DiferenciaParaIndicador>()).Where(x => x != null))
            {
                List<string> errores = ErroresDe(d);
                Sumar(d.Preparador, errores.Contains(d.Origen?.Trim().ToUpperInvariant()));
                Sumar(d.UsuarioRecepcion, errores.Contains(d.Destino?.Trim().ToUpperInvariant()));
            }
            return porUsuario.Values.OrderBy(u => u.Usuario).ToList();
        }
    }

    /// <summary>NestoAPI#906: un caso decidido por la regla y todavía sin verdad, para buscarla en los inventarios.</summary>
    public class CasoSinVerdad
    {
        public int DiscusionId { get; set; }
        public string Producto { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public int Unidades { get; set; }
        /// <summary>Desde cuándo falta (la recepción): solo vale un inventario posterior.</summary>
        public DateTime Fecha { get; set; }
    }

    public class VerdadAsignada
    {
        public int DiscusionId { get; set; }
        public string Almacen { get; set; }
        public int Inventario { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>
    /// NestoAPI#906, la verdad a posteriori (pura): un inventario CONTABILIZADO posterior del mismo producto con SOBRANTE de al menos las
    /// unidades discutidas en el origen o en el destino demuestra que ese almacén la tenía (origen: no salió; destino: llegó y no se dio
    /// de alta). Cautelas: cada fila de inventario «gasta» una sola vez (casos de más antiguo a más nuevo); si sobra en los dos, no se
    /// decide (y no se gasta nada); los que ya tienen verdad no llegan aquí.
    /// </summary>
    public static class ReglaVerdadInventario
    {
        public static List<VerdadAsignada> Asignar(IEnumerable<CasoSinVerdad> casos, IEnumerable<SobranteInventario> sobrantes, IEnumerable<int> yaGastados)
        {
            var gastados = new HashSet<int>(yaGastados ?? Enumerable.Empty<int>());
            List<SobranteInventario> disponibles = (sobrantes ?? Enumerable.Empty<SobranteInventario>())
                .Where(s => s != null && s.Sobrante > 0 && !gastados.Contains(s.NumOrden))
                .OrderBy(s => s.Fecha).ThenBy(s => s.NumOrden).ToList();
            var resultado = new List<VerdadAsignada>();
            foreach (CasoSinVerdad caso in (casos ?? Enumerable.Empty<CasoSinVerdad>()).Where(c => c != null && c.Unidades > 0)
                .OrderBy(c => c.Fecha).ThenBy(c => c.DiscusionId))
            {
                SobranteInventario Buscar(string almacen) => disponibles.FirstOrDefault(s => !gastados.Contains(s.NumOrden)
                    && string.Equals(s.Producto?.Trim(), caso.Producto?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Almacen?.Trim(), almacen?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && s.Fecha > caso.Fecha && s.Sobrante >= caso.Unidades);
                SobranteInventario enOrigen = Buscar(caso.Origen);
                SobranteInventario enDestino = Buscar(caso.Destino);
                if ((enOrigen == null) == (enDestino == null))
                {
                    continue; // en ninguno, o en los dos: no se decide
                }
                SobranteInventario usado = enOrigen ?? enDestino;
                _ = gastados.Add(usado.NumOrden);
                resultado.Add(new VerdadAsignada
                {
                    DiscusionId = caso.DiscusionId,
                    Almacen = (enOrigen != null ? caso.Origen : caso.Destino)?.Trim().ToUpperInvariant(),
                    Inventario = usado.NumOrden,
                    Fecha = usado.Fecha
                });
            }
            return resultado;
        }
    }

    public class AciertoPorPasoDTO
    {
        public string Paso { get; set; }
        public int Unidades { get; set; }
        public int ConVerdad { get; set; }
        public int Aciertos { get; set; }
        /// <summary>Aciertos / con verdad, en % (null sin verdad).</summary>
        public decimal? PorcentajeAcierto => ConVerdad == 0 ? (decimal?)null : Math.Round(100M * Aciertos / ConVerdad, 1);
    }

    public class ResumenVerdadDesacuerdoDTO
    {
        public DateTime Desde { get; set; }
        public int Casos { get; set; }
        public int CasosConVerdad { get; set; }
        public int Unidades { get; set; }
        public int UnidadesConVerdad { get; set; }
        public List<AciertoPorPasoDTO> PorPaso { get; set; } = new List<AciertoPorPasoDTO>();
        /// <summary>Lo que hay para entrenar (unidades decididas con verdad) frente al mínimo del modelo.</summary>
        public int EjemplosParaModelo { get; set; }
        public int MinimoEjemplosModelo { get; set; } = ModeloImputacionDesacuerdo.MINIMO_EJEMPLOS;
        public string QuienManda { get; set; } = ModeloImputacionDesacuerdo.REGLA;
    }

    /// <summary>NestoAPI#906: el % de acierto de la regla por paso, sobre las unidades decididas que ya tienen verdad. Puro.</summary>
    public static class ResumenVerdadDesacuerdo
    {
        public static ResumenVerdadDesacuerdoDTO Calcular(IEnumerable<ImputacionDesacuerdoFila> filas, DateTime desde)
        {
            List<ImputacionDesacuerdoFila> decididas = (filas ?? Enumerable.Empty<ImputacionDesacuerdoFila>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.AlmacenImputado)).ToList();
            bool ConVerdad(ImputacionDesacuerdoFila f) => !string.IsNullOrWhiteSpace(f.ErrorDemostradoDe);
            bool Acierto(ImputacionDesacuerdoFila f) => string.Equals(f.ErrorDemostradoDe?.Trim(), f.AlmacenImputado?.Trim(), StringComparison.OrdinalIgnoreCase);
            return new ResumenVerdadDesacuerdoDTO
            {
                Desde = desde,
                Casos = decididas.Select(f => f.DiscusionId).Distinct().Count(),
                CasosConVerdad = decididas.Where(ConVerdad).Select(f => f.DiscusionId).Distinct().Count(),
                Unidades = decididas.Count,
                UnidadesConVerdad = decididas.Count(ConVerdad),
                EjemplosParaModelo = decididas.Count(ConVerdad),
                PorPaso = PasosImputacionDesacuerdo.TODOS.Select(paso =>
                {
                    List<ImputacionDesacuerdoFila> delPaso = decididas.Where(f => string.Equals(f.Paso, paso, StringComparison.OrdinalIgnoreCase)).ToList();
                    return new AciertoPorPasoDTO
                    {
                        Paso = paso,
                        Unidades = delPaso.Count,
                        ConVerdad = delPaso.Count(ConVerdad),
                        Aciertos = delPaso.Count(f => ConVerdad(f) && Acierto(f))
                    };
                }).ToList()
            };
        }
    }

    /// <summary>
    /// NestoAPI#906: SITIO PREPARADO para el modelo (patrón ModeloPlazos / ModeloTiemposParada), SIN implementar: hoy siempre manda la
    /// regla. Cuando haya ejemplos (unidades decididas con verdad, <see cref="MINIMO_EJEMPLOS"/> como mínimo), un job fuera de horario
    /// entrenaría con los rasgos de dbo.DiferenciasReposicionImputaciones, validaría contra la regla (puerta de calidad: no empeorar el %
    /// de acierto) y el interruptor «(defecto)» <see cref="CLAVE_QUIEN_MANDA"/> = «Regla» | «Modelo» elegiría quién decide.
    /// </summary>
    public static class ModeloImputacionDesacuerdo
    {
        public const int MINIMO_EJEMPLOS = 200;
        public const string CLAVE_QUIEN_MANDA = "Reposiciones.DesacuerdoQuienManda";
        public const string REGLA = "Regla";
        public const string MODELO = "Modelo";

        /// <summary>Quién decide: hoy SIEMPRE la regla (no hay modelo entrenado), diga lo que diga el interruptor.</summary>
        public static string QuienManda(string interruptor, bool hayModelo)
            => hayModelo && string.Equals(interruptor?.Trim(), MODELO, StringComparison.OrdinalIgnoreCase) ? MODELO : REGLA;
    }
}
