using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models.ApuntesBanco;
using System.Data.Entity;
using NestoAPI.Models.Bancos;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class ContabilidadService : IContabilidadService
    {
        public async Task<int> ContabilizarDiario(string empresa, string diario, string usuario)
        {
            using (var db = new NVEntities())
            {
                return await ContabilizarDiario(db, empresa, diario, usuario);
            }
        }

        public async Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario)
        {
            var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
            {
                Value = diario
            };
            var usuarioParametro = new SqlParameter("@Usuario", SqlDbType.Char, 30)
            {
                Value = usuario
            };
            //var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync("EXEC prdContabilizar @Empresa, @Diario", empresaParametro, diarioParametro);
            //return resultadoProcedimiento;
            var resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };
            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdContabilizar @Empresa, @Diario, @Usuario", resultadoParametro, empresaParametro, diarioParametro, usuarioParametro);
                // Obtener el valor de retorno del parámetro
                var resultadoProcedimiento = (int)resultadoParametro.Value;
                return resultadoProcedimiento;
            } catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        public async Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas)
        {            
            foreach (var linea in lineas)
            {
                if (linea.FechaVto == null || linea.FechaVto == DateTime.MinValue)
                {
                    linea.FechaVto = new DateTime(linea.Fecha.Year, linea.Fecha.Month, linea.Fecha.Day);
                }
                linea.Fecha_Modificación = DateTime.Now;
                db.PreContabilidades.Add(linea);
            }
            try
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
                return lineas.Last().Nº_Orden;
            }
            catch(Exception ex) {
                throw new Exception("No se ha podido contabilizar el diario",ex);
            }
            
        }

        public async Task<int> CrearLineas(List<PreContabilidad> lineas)
        {
            using (var db = new NVEntities())
            {
                return await CrearLineas(db, lineas);
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas)
        {
            using (var db = new NVEntities())
            {
                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        int resultado = await CrearLineasYContabilizarDiario(lineas, db);
                        if (resultado > 0)
                        {
                            transaction.Commit();
                        }
                        else
                        {
                            transaction.Rollback();
                        }
                        return resultado;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return 0;
                    }
                }
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas, NVEntities db)
        {            
            // Ojo, aquí hay que entrar ya en transacción y hacer try/catch con el rollback y el commit desde el método que llama            
            await CrearLineas(db, lineas);
            var resultado = await ContabilizarDiario(db, lineas[0].Empresa, lineas[0].Diario, lineas[0].Usuario);
            return resultado;                
        }

        public async Task<bool> PersistirCuaderno43(ContenidoCuaderno43 contenido)
        {
            using (var db = new NVEntities())
            {
                var fechaFichero = contenido.Apuntes.First().FechaOperacion;
                if (db.ApuntesBancarios.Any(c => c.FechaOperacion == fechaFichero))
                {
                    throw new Exception($"Ya se ha contabilizado el fichero del día {fechaFichero.ToShortDateString()}");
                }
                try
                {
                    var fichero = new FicheroCuaderno43();

                    // Si en algún momento tenemos dos cuentas en el mismo fichero, lo trataríamos como dos ficheros
                    // Asignar valores de la clase RegistroCabeceraCuenta a la instancia de FicheroCuaderno43
                    fichero.ClaveEntidad = contenido.Cabecera.ClaveEntidad;
                    fichero.ClaveOficina = contenido.Cabecera.ClaveOficina;
                    fichero.NumeroCuenta = contenido.Cabecera.NumeroCuenta;
                    fichero.FechaInicial = contenido.Cabecera.FechaInicial;
                    fichero.FechaFinal = contenido.Cabecera.FechaFinal;
                    fichero.ClaveDebeOHaber = contenido.Cabecera.ClaveDebeOHaber;
                    fichero.ImporteSaldoInicial = contenido.Cabecera.ImporteSaldoInicial;
                    fichero.ClaveDivisa = contenido.Cabecera.ClaveDivisa;
                    fichero.ModalidadInformacion = contenido.Cabecera.ModalidadInformacion;
                    fichero.NombreAbreviado = contenido.Cabecera.NombreAbreviado;

                    // Asignar valores de la clase RegistroFinalCuenta a la instancia de FicheroCuaderno43
                    // Esto está mal en fichero son re
                    fichero.TotalImportesDebe = contenido.FinalCuenta.TotalImportesDebe;
                    fichero.NumeroApuntesHaber = contenido.FinalCuenta.NumeroApuntesHaber;
                    fichero.TotalImportesHaber = contenido.FinalCuenta.TotalImportesHaber;
                    fichero.CodigoSaldoFinal = contenido.FinalCuenta.CodigoSaldoFinal;
                    fichero.SaldoFinal = contenido.FinalCuenta.SaldoFinal;

                    // Asignar valores adicionales o predeterminados
                    fichero.NumeroRegistros = contenido.FinalFichero.NumeroRegistros;
                    fichero.Usuario = contenido.Usuario; 
                    fichero.FechaCreacion = DateTime.Now; // Fecha actual                    

                    // Iterar sobre cada ApunteBancarioDTO en la lista contenido.Apuntes
                    foreach (var apunteDto in contenido.Apuntes)
                    {
                        // Crear una nueva instancia de ApunteBancario
                        var apunteBancario = new ApunteBancario();

                        // Asignar valores de ApunteBancarioDTO a ApunteBancario
                        apunteBancario.ClaveOficinaOrigen = apunteDto.ClaveOficinaOrigen;
                        apunteBancario.FechaOperacion = apunteDto.FechaOperacion;
                        apunteBancario.FechaValor = apunteDto.FechaValor;
                        apunteBancario.ConceptoComun = apunteDto.ConceptoComun;
                        apunteBancario.ConceptoPropio = apunteDto.ConceptoPropio;
                        apunteBancario.ClaveDebeOHaberMovimiento = apunteDto.ClaveDebeOHaberMovimiento;
                        apunteBancario.ImporteMovimiento = apunteDto.ImporteMovimiento;
                        apunteBancario.NumeroDocumento = apunteDto.NumeroDocumento;
                        apunteBancario.Referencia1 = apunteDto.Referencia1;
                        apunteBancario.Referencia2 = apunteDto.Referencia2;
                        apunteBancario.Usuario = contenido.Usuario;
                        apunteBancario.FechaCreacion = DateTime.Now;

                        // Iterar sobre cada ConceptoComplementario en apunteDto.RegistrosConcepto
                        foreach (var concepto in apunteDto.RegistrosConcepto)
                        {
                            // Crear una nueva instancia de RegistroComplementarioConcepto
                            var registroConcepto = new RegistroComplementarioConcepto();

                            // Asignar valores de ConceptoComplementario a RegistroComplementarioConcepto
                            registroConcepto.CodigoDato = concepto.CodigoDatoConcepto;
                            registroConcepto.Concepto = concepto.Concepto;
                            registroConcepto.Concepto2 = concepto.Concepto2;
                            registroConcepto.Usuario = contenido.Usuario;
                            registroConcepto.FechaCreacion = DateTime.Now;


                            // Agregar RegistroComplementarioConcepto a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioConceptoes.Add(registroConcepto);
                        }

                        // Si hay un objeto ImporteEquivalencia en apunteDto
                        if (apunteDto.ImporteEquivalencia != null && apunteDto.ImporteEquivalencia.ImporteEquivalencia != 0)
                        {
                            // Crear una nueva instancia de RegistroComplementarioEquivalencia
                            var registroEquivalencia = new RegistroComplementarioEquivalencia();

                            // Asignar valores de EquivalenciaDivisas a RegistroComplementarioEquivalencia
                            registroEquivalencia.CodigoDato = apunteDto.ImporteEquivalencia.CodigoDatoEquivalencia;
                            registroEquivalencia.ClaveDivisaOrigen = apunteDto.ImporteEquivalencia.ClaveDivisaOrigen;
                            registroEquivalencia.ImporteEquivalencia = apunteDto.ImporteEquivalencia.ImporteEquivalencia;
                            registroEquivalencia.Usuario = contenido.Usuario;
                            registroEquivalencia.FechaCreacion = DateTime.Now;

                            // Agregar RegistroComplementarioEquivalencia a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioEquivalencias.Add(registroEquivalencia);
                        }

                        // Agregar apunteBancario a la colección de apuntes bancarios de fichero
                        fichero.ApuntesBancarios.Add(apunteBancario);
                    }

                    // Agregar la instancia de FicheroCuaderno43 al contexto y guardar los cambios
                    db.FicherosCuaderno43.Add(fichero);

                    await db.SaveChangesAsync();
                    return true;
                } 
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public async Task<bool> PersistirMovimientosTPV(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (var db = new NVEntities())
            {
                var fechaFichero = movimientosTPV.First().FechaOperacion;
                if (db.MovimientosTPV.Any(c => c.FechaOperacion == fechaFichero))
                {
                    throw new Exception($"Ya se ha contabilizado el fichero de tarjetas del día {fechaFichero.ToShortDateString()}");
                }

                try
                {
                    foreach (var movimiento in movimientosTPV)
                    {
                        var nuevoMovimiento = new MovimientoTPV();
                        nuevoMovimiento.ModoCaptura = movimiento.ModoCaptura;
                        nuevoMovimiento.Sesion = movimiento.Sesion;
                        nuevoMovimiento.Terminal = movimiento.Terminal;
                        nuevoMovimiento.FechaCaptura = movimiento.FechaCaptura;
                        nuevoMovimiento.FechaOperacion = movimiento.FechaOperacion;
                        nuevoMovimiento.ImporteOperacion = movimiento.ImporteOperacion;
                        nuevoMovimiento.ImporteComision = movimiento.ImporteComision;
                        nuevoMovimiento.ImporteAbono = movimiento.ImporteAbono;
                        nuevoMovimiento.CodigoMoneda = movimiento.CodigoMoneda;
                        nuevoMovimiento.Comentarios = movimiento.Comentarios;
                        nuevoMovimiento.Usuario = movimiento.Usuario;
                        nuevoMovimiento.FechaCreacion = DateTime.Now;
                        db.MovimientosTPV.Add(nuevoMovimiento);                        
                    }
                    await db.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    throw new Exception("No se ha podido persistir el fichero de tarjetas", ex);
                }
                
            }
        }

        public async Task<decimal> SaldoFinal(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (var db = new NVEntities())
            {
                var contenido = await db.FicherosCuaderno43
                    .Where(c => c.ClaveEntidad == entidad && c.ClaveOficina == oficina && c.NumeroCuenta == cuenta && c.FechaFinal <= fecha)
                    .OrderByDescending(c => c.FechaFinal)
                    .FirstOrDefaultAsync();

                return contenido != null ? contenido.SaldoFinal : 0m;
            }
        }

        public async Task<decimal> SaldoInicial(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (var db = new NVEntities())
            {
                var contenido = await db.FicherosCuaderno43
                    .Where(c => c.ClaveEntidad == entidad && c.ClaveOficina == oficina && c.NumeroCuenta == cuenta && c.FechaInicial == fecha).SingleAsync().ConfigureAwait(false);
                return contenido.ImporteSaldoInicial;
            }
        }

        public async Task<List<MovimientoTPVDTO>> LeerMovimientosTPV(DateTime fechaCaptura, string tipoDatafono)
        {
            using (var db = new NVEntities())
            {
                try
                {
                    var movimientos = await db.MovimientosTPV
                        .Where(m => m.FechaCaptura == fechaCaptura && m.ModoCaptura == tipoDatafono)
                        .Select(m => new MovimientoTPVDTO
                        {
                            ModoCaptura = m.ModoCaptura,
                            Sesion = m.Sesion,
                            Terminal = m.Terminal,
                            FechaCaptura = m.FechaCaptura,
                            FechaOperacion = m.FechaOperacion,
                            ImporteOperacion = m.ImporteOperacion,
                            ImporteComision = m.ImporteComision,
                            ImporteAbono = m.ImporteAbono,
                            CodigoMoneda = m.CodigoMoneda,
                            Comentarios = m.Comentarios,
                            FechaCreacion = m.FechaCreacion,
                            Usuario = m.Usuario
                        })
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (var movimiento in movimientos)
                    {
                        movimiento.TextoModoCaptura = MovimientoTPVDTO.GetModoCapturaDescription(movimiento.ModoCaptura);
                        movimiento.UsuarioTerminal = ObtenerUsuarioTerminal(movimiento.Terminal);
                    }

                    return movimientos;
                } 
                catch (Exception ex)
                {
                    throw new Exception("No se han podido leer los movimientos de los TPV", ex);
                }

            }
        }
        private Dictionary<string, string> TerminalesUsuarios = new Dictionary<string, string>()
        {
            { "91900804273", "Paloma" },
            { "91900804275", "Elena" },
            { "26617120788", "Laura Camacho" },
            { "00346609775", "Kelma" },
            { "00132951570", "Web" },
            { "00232951570", "Paygold" },
            { "00025537534", "Pilar" },
            { "51570001329", "Bizum tienda online" },
            { "51570002329", "Bizum Paygold" },
            { "00022126270", "Almacén" },
        };
        private string ObtenerUsuarioTerminal(string terminal)
        {
            if (TerminalesUsuarios.TryGetValue(terminal, out string usuario))
            {
                return usuario;
            }
            return string.Empty; // O un valor predeterminado si prefieres
        }

        public async Task ContabilizarComisionesTarjetas(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (var db = new NVEntities())
            {
                try
                {
                    var lineas = new List<PreContabilidad>();
                    decimal sumaComisiones = 0;
                    // Si algún día trabajamos con un banco que no sea Caixa tenemos que crear un campo en MovimientoTPVDTO que sea el banco
                    // o si no pasar el banco a este método como un parámetro desde el controlador
                    var banco = await db.Bancos.SingleAsync(b => b.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && b.Entidad == "2100");
                    var fechaIngreso = movimientosTPV.First().FechaCaptura.AddDays(1);
                    int numeroCobros = 0;
                    foreach (var movimiento in movimientosTPV.Where(m => m.ImporteComision != 0))
                    {
                        sumaComisiones += movimiento.ImporteComision;
                        decimal porcentajeComision = movimiento.ImporteComision / movimiento.ImporteOperacion;
                        var nuevaLinea = new PreContabilidad
                        {
                            Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                            Asiento = 1,
                            Fecha = fechaIngreso,
                            TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                            TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                            Nº_Cuenta = Constantes.Cuentas.COMISIONES_BANCO_COBRO_TPV,
                            Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({porcentajeComision:p})",
                            Nº_Documento = movimiento.Terminal.Length > 10 ? movimiento.Terminal.Substring(movimiento.Terminal.Length - 10) : movimiento.Terminal,
                            Debe = movimiento.ImporteComision,
                            Asiento_Automático = true,
                            CentroCoste = "CA",
                            Delegación = "ALG",
                            Departamento = "ADM",
                            Usuario = movimiento.Usuario,
                            Fecha_Modificación = DateTime.Now
                        };
                        lineas.Add(nuevaLinea);
                        numeroCobros++;
                    }
                    var contrapartida = new PreContabilidad
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                        Asiento = 1,
                        Fecha = fechaIngreso,
                        TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                        TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                        Nº_Cuenta = banco.Cuenta_Contable,
                        Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({numeroCobros} cobros)",
                        Nº_Documento = $"TPV_{fechaIngreso.ToString("ddMMyy")}",
                        Haber = sumaComisiones,
                        Asiento_Automático = true,
                        Delegación = "ALG",
                        Usuario = movimientosTPV.First().Usuario,
                        Fecha_Modificación = DateTime.Now
                    };
                    lineas.Add(contrapartida);

                    await CrearLineasYContabilizarDiario(lineas);
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al contabilizar las comisiones de las tarjetas", ex);
                }
            }
        }

        public async Task<int> NumeroRecibosRemesa(int remesa)
        {
            using (var db = new NVEntities())
            {
                var recibos = db.ExtractosCliente.Where(e => e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && e.Remesa == remesa);
                var numeroRecibos = await recibos.CountAsync();
                return numeroRecibos;
            }
        }

        public async Task<string> LeerProveedorPorNombre(string nombreProveedor)
        {
            using (var db = new NVEntities())
            {
                // nombreProveedor = LimpiarNombreProveedor(nombreProveedor); // quitar SL, SA, etc...
                var proveedor = (await db.Proveedores.SingleOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Nombre.Contains(nombreProveedor)))?.Número;
                if (string.IsNullOrEmpty(proveedor))
                {
                    return string.Empty;
                }
                else
                {
                    return proveedor.Trim();
                }
            }
        }

        public async Task<ExtractoProveedorDTO> PagoPendienteUnico(string proveedor, decimal importe)
        {
            using (var db = new NVEntities())
            {
                var pagoPendiente = await db.ExtractosProveedor
                    .AsNoTracking()
                    .Where(e =>
                        e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                        e.Número == proveedor &&
                        e.ImportePdte == importe
                    )
                    .Select(e => new ExtractoProveedorDTO
                    {
                        Id = e.NºOrden,
                        Empresa = e.Empresa.Trim(),
                        Proveedor = e.Número.Trim(),
                        Contacto = e.Contacto.Trim(),
                        Documento = e.NºDocumento.Trim(),
                        DocumentoProveedor = e.NºDocumentoProv.Trim(),
                        Delegacion = e.Delegación.Trim(),
                        FormaVenta = e.FormaVenta.Trim()
                    })
                    .SingleOrDefaultAsync();
                
                return pagoPendiente;
            }
        }

        public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    var movimientoSinPuntearDebe = await db.Contabilidades
                        .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false);
                    var movimientoSinPuntearHaber = await db.Contabilidades
                        .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false);
                    movimientoSinPuntearDebe.Punteado = true;
                    movimientoSinPuntearHaber.Punteado = true;
                    int movimientosModificados = await db.SaveChangesAsync();
                    return movimientosModificados == 2; // Comprobamos que se hayan punteado los dos
                }                
            }
            catch
            {
                return false;
            }
        }
    }
}