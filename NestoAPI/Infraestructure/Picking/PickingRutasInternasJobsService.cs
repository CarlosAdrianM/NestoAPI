using Hangfire;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>Lo que ha hecho una pasada del picking automático de las rutas internas.</summary>
    public enum ResultadoPickingRutasInternas
    {
        /// <summary>El interruptor está apagado: no se ha hecho nada.</summary>
        Apagado,
        /// <summary>Hoy no es laborable en Algete: no se ha hecho nada.</summary>
        NoLaborable,
        /// <summary>Se ha lanzado el picking (haya salido algo o no; el almacén recibe el correo de siempre si no).</summary>
        Lanzado
    }

    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): picking AUTOMÁTICO de las rutas internas (las de <c>RutasPropiasConfiguradas</c>, hoy AT y
    /// 16) a las 9:00, para que la furgoneta salga sin esperar al de las 11. Es el mismo picking que el botón «Solo nuestra ruta»
    /// de Nesto (sugerencia 854, <c>api/Picking/RutasPropias</c>: mismas rutas, mismo <see cref="GestorPicking.SacarPicking(List{Ruta}, DateTime)"/>)
    /// con la forma del automático de las 11 (<see cref="PickingJobsService"/>): el horizonte de entrega es un dato (hoy), el
    /// almacén recibe el correo si no sale nada o falla, sin reintentos y sin solaparse (applock de <see cref="GestorPicking"/>).
    /// <para>De los pedidos con día de reparto por código postal solo salen los que tocan hoy (<see cref="GestorRutaInternaPicking"/>);
    /// los de la 16 sin día, como siempre (#588: se preparan hoy y se entregan el laborable siguiente). El de las 11 sigue igual
    /// para todo lo demás y saca después lo que quede.</para>
    /// <para>Nace APAGADO: interruptor <see cref="Constantes.ParametrosUsuario.PICKING_AUTOMATICO_RUTAS_INTERNAS"/> = "1" bajo
    /// «(defecto)» en ParametrosUsuario (no en el Web.config, que vuelve a su valor al publicar). Solo laborables de Algete.</para>
    /// </summary>
    public class PickingRutasInternasJobsService
    {
        public const string JOB_ID = "picking-rutas-internas";
        public const string VALOR_ENCENDIDO = "1";

        private readonly ILectorParametrosUsuario lectorParametros;
        private readonly Func<DateTime, bool> esFestivo;
        private readonly Func<DateTime> reloj;
        private readonly Func<List<Ruta>> leerRutasEmpresa;
        private readonly Func<List<Ruta>, DateTime, List<PedidoPicking>> sacarPicking;
        private readonly Action<Exception, DateTime> avisar;

        public PickingRutasInternasJobsService(ILectorParametrosUsuario lectorParametros, Func<DateTime, bool> esFestivo, Func<DateTime> reloj,
            Func<List<Ruta>> leerRutasEmpresa, Func<List<Ruta>, DateTime, List<PedidoPicking>> sacarPicking, Action<Exception, DateTime> avisar)
        {
            this.lectorParametros = lectorParametros ?? throw new ArgumentNullException(nameof(lectorParametros));
            this.esFestivo = esFestivo ?? throw new ArgumentNullException(nameof(esFestivo));
            this.reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
            this.leerRutasEmpresa = leerRutasEmpresa ?? throw new ArgumentNullException(nameof(leerRutasEmpresa));
            this.sacarPicking = sacarPicking ?? throw new ArgumentNullException(nameof(sacarPicking));
            this.avisar = avisar ?? ((ex, momento) => { });
        }

        /// <summary>Punto de entrada para Hangfire. Void a propósito (NestoAPI#416) y sin reintentos (como el de las 11).</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static void SacarPickingRutasInternasJob()
        {
            _ = CrearPorDefecto().Ejecutar();
        }

        internal static PickingRutasInternasJobsService CrearPorDefecto()
        {
            return new PickingRutasInternasJobsService(
                new LectorParametrosUsuario(),
                f => GestorFestivos.EsFestivo(f, Constantes.Almacenes.ALGETE),
                () => DateTime.Now,
                LeerRutasEmpresaDeLaBaseDeDatos,
                (rutas, fechaPicking) =>
                {
                    GestorPicking gestor = PickingJobsService.CrearGestor();
                    gestor.SacarPicking(rutas, fechaPicking);
                    return gestor.PedidosEnPicking();
                },
                (ex, momento) => PickingJobsService.CrearAvisador().Avisar(ex, momento));
        }

        private static List<Ruta> LeerRutasEmpresaDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db.Rutas.AsNoTracking().Where(r => r.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO).ToList();
            }
        }

        /// <summary>¿Está encendido? Solo con "1" bajo «(defecto)»; cualquier otra cosa (o un fallo al leer) = apagado.</summary>
        public bool EstaEncendido()
        {
            try
            {
                string valor = lectorParametros.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.PICKING_AUTOMATICO_RUTAS_INTERNAS);
                return valor?.Trim() == VALOR_ENCENDIDO;
            }
            catch
            {
                return false;
            }
        }

        public ResultadoPickingRutasInternas Ejecutar()
        {
            if (!EstaEncendido())
            {
                return ResultadoPickingRutasInternas.Apagado;
            }
            DateTime ahora = reloj();
            if (esFestivo(ahora.Date))
            {
                return ResultadoPickingRutasInternas.NoLaborable;
            }
            try
            {
                // Las mismas rutas que el botón «Solo nuestra ruta» (PickingController.FiltrarRutasPropias)
                List<Ruta> rutas = Controllers.PickingController.FiltrarRutasPropias(leerRutasEmpresa());
                // El horizonte es un DATO: hoy. Los de día de reparto, solo los que tocan hoy (lo decide el picking).
                _ = sacarPicking(rutas, ahora.Date);
            }
            catch (Exception ex)
            {
                avisar(ex, reloj());
                if (AvisadorPickingAutomatico.EsPickingSinTrabajo(ex) || AvisadorPickingAutomatico.EsSoloClientesCerrados(ex))
                {
                    return ResultadoPickingRutasInternas.Lanzado;
                }
                throw;
            }
            return ResultadoPickingRutasInternas.Lanzado;
        }
    }
}
