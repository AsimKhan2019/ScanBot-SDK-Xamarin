﻿using System;
using System.IO;
using System.Threading.Tasks;

using Android.App;
using Android.Widget;
using Android.OS;
using Android.Graphics;
using Android.Content;

using ScanbotSDK.Xamarin;
using ScanbotSDK.Xamarin.Android;

using Java.Util;

using AndroidNetUri = Android.Net.Uri;
using AndroidOS = Android.OS;
using Android.Util;
using Net.Doo.Snap.Process;
using Net.Doo.Snap.Persistence;
using Net.Doo.Snap.Persistence.Cleanup;
using Net.Doo.Snap.Blob;
using Net.Doo.Snap.Process.Draft;
using System.Collections.Generic;
using Net.Doo.Snap.Entity;
using Net.Doo.Snap.Util;
using Android.Preferences;
using IO.Scanbot.Sdk.UI.View.Mrz;
using IO.Scanbot.Sdk.UI.View.Mrz.Configuration;
using IO.Scanbot.Mrzscanner.Model;
using IO.Scanbot.Sdk.UI.View.Barcode.Configuration;
using IO.Scanbot.Sdk.UI.View.Barcode;
using IO.Scanbot.Sdk.Barcode.Entity;

namespace scanbotsdkexamplexamarin.Droid
{
    [Activity(Label = "Scanbot SDK Example Xamarin", MainLauncher = true, Icon = "@mipmap/icon", 
              ScreenOrientation = Android.Content.PM.ScreenOrientation.Portrait)]
    public class MainActivity : Activity
    {
        public static TempImageStorage TempImageStorage = new TempImageStorage();

        static string LOG_TAG = typeof(MainActivity).Name;

        const int REQUEST_SB_SCANNING_UI = 4711;
        const int REQUEST_SB_CROPPING_UI = 4712;
        const int REQUEST_SYSTEM_GALLERY = 4713;
        const int REQUEST_SB_MRZ_SCANNER = 4714;
        const int REQUEST_SB_BARCODE_SCANNER = 4715;

        const int BIG_THUMB_MAX_W = 800, BIG_THUMB_MAX_H = 800;

        AndroidNetUri documentImageUri, originalImageUri;

        ImageView imageView;

        Button performOcrButton;

        DocumentProcessor documentProcessor;
        PageFactory pageFactory;
        IDocumentDraftExtractor documentDraftExtractor;
        ITextRecognition textRecognition;
        Cleaner cleaner;
        BlobManager blobManager;
        BlobFactory blobFactory;

        static List<Language> ocrLanguages = new List<Language>();

        static MainActivity()
        {
            // set required OCR languages ...
            ocrLanguages.Add(Language.Eng); // english
            ocrLanguages.Add(Language.Deu); // german
        }


        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.Main);

            imageView = FindViewById<ImageView>(Resource.Id.imageView);

            InitScanbotSDKDependencies();

            AssignCopyrightText();
            AssignStartCameraButtonHandler();
            AssingCroppingUIButtonHandler();
            AssignApplyImageFilterButtonHandler();
            AssignDocumentDetectionButtonHandler();
            AssignCreatePdfButtonHandler();
            AssignCreateTiffButtonHandler();
            AssignOcrButtonsHandler();
            AssignMrzScannerButtonHandler();
            AssignBarcodeScannerButtonHandler();
        }


        void InitScanbotSDKDependencies()
        {
            var scanbotSDK = new Net.Doo.Snap.ScanbotSDK(this);
            documentProcessor = scanbotSDK.DocumentProcessor();
            pageFactory = scanbotSDK.PageFactory();
            documentDraftExtractor = scanbotSDK.DocumentDraftExtractor();
            textRecognition = scanbotSDK.TextRecognition();
            cleaner = scanbotSDK.Cleaner();
            blobManager = scanbotSDK.BlobManager();
            blobFactory = scanbotSDK.BlobFactory();
        }

        List<Blob> OcrBlobs()
        {
            // Create a collection of required OCR blobs:
            var blobs = new List<Blob>();

            // Language detector blobs of the Scanbot SDK. (see "language_classifier_blob_path" in AndroidManifest.xml!)
            foreach (var b in blobFactory.LanguageDetectorBlobs())
            {
                blobs.Add(b);
            }

            // OCR blobs of languages (see "ocr_blobs_path" in AndroidManifest.xml!)
            foreach (var lng in ocrLanguages)
            {
                foreach (var b in blobFactory.OcrLanguageBlobs(lng))
                {
                    blobs.Add(b);
                }
            }

            return blobs;
        }

        void FetchOcrBlobFiles()
        {
            // Fetch OCR blob files from the sources defined in AndroidManifest.xml
            Task.Run(() =>
            {
                try
                {
                    foreach (var blob in OcrBlobs())
                    {
                        if (!blobManager.IsBlobAvailable(blob))
                        {
                            DebugLog("Fetching OCR blob file: " + blob);
                            blobManager.Fetch(blob, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorLog("Error fetching OCR blob files", e);
                }
            });
        }


        void AssignCopyrightText()
        {
            var copyrightTextView = FindViewById<TextView>(Resource.Id.copyrightTextView);
            copyrightTextView.Text = "Copyright (c) "+DateTime.Now.Year.ToString()+" doo GmbH. All rights reserved.";
        }

        void AssignStartCameraButtonHandler()
        {
            var scanningUIButton = FindViewById<Button>(Resource.Id.scanningUIButton);
            scanningUIButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }

                Intent intent = new Intent(this, typeof(CameraViewDemoActivity));
                StartActivityForResult(intent, REQUEST_SB_SCANNING_UI);
            };
        }

        void AssignApplyImageFilterButtonHandler()
        {
            var applyImageFilterButton = FindViewById<Button>(Resource.Id.applyImageFilterButton);
            applyImageFilterButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }
                if (!CheckDocumentImage()) { return; }

                var transaction = FragmentManager.BeginTransaction();
                var dialogFragment = new ImageFilterDialog(ApplyImageFilter);
                dialogFragment.Show(transaction, "ImageFilterDialog");
            };
        }

        void ApplyImageFilter(ImageFilter filter)
        {
            DebugLog("Applying image filter "+filter+" on image: " + documentImageUri);
            try
            {
                Task.Run(() =>
                {
                    // The SDK call is sync!
                    var resultImage = SBSDK.ApplyImageFilter(documentImageUri, filter);
                    documentImageUri = TempImageStorage.AddImage(resultImage);
                    ShowImageView(resultImage);
                });
            }
            catch (Exception e)
            {
                ErrorLog("Error applying image filter", e);
            }
        }

        void AssignDocumentDetectionButtonHandler()
        {
            var documentDetectionButton = FindViewById<Button>(Resource.Id.documentDetectionButton);
            documentDetectionButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }

                // Select image from gallery and run document detection
                var imageIntent = new Intent();
                imageIntent.SetType("image/*");
                imageIntent.SetAction(Intent.ActionGetContent);
                imageIntent.PutExtra(Intent.ExtraLocalOnly, true);
                StartActivityForResult(Intent.CreateChooser(imageIntent, "Select photo"), REQUEST_SYSTEM_GALLERY);
            };
        }

        void AssingCroppingUIButtonHandler()
        {
            var croppingUIButton = FindViewById<Button>(Resource.Id.croppingUIButton);
            croppingUIButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }
                if (!CheckOriginalImage()) { return; }

                Intent intent = new Intent(this, typeof(CroppingImageDemoActivity));
                intent.PutExtra(CroppingImageDemoActivity.EXTRAS_ARG_IMAGE_FILE_URI, originalImageUri.ToString());
                StartActivityForResult(intent, REQUEST_SB_CROPPING_UI);
            };
        }

        void AssignCreatePdfButtonHandler()
        {
            var createPdfButton = FindViewById<Button>(Resource.Id.createPdfButton);
            createPdfButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }
                if (!CheckDocumentImage()) { return; }

                DebugLog("Starting PDF creation...");
                var pdfOutputUri = GenerateRandomFileUriInExternalStorage(".pdf");

                Task.Run(() =>
                {
                    try
                    {
                        var images = new AndroidNetUri[] { documentImageUri }; // add more images for PDF pages here
                        // The SDK call is sync!
                        SBSDK.CreatePDF(images, pdfOutputUri);
                        DebugLog("PDF file created: " + pdfOutputUri);
                        OpenPDFFile(pdfOutputUri);
                    }
                    catch (Exception e)
                    {
                        ErrorLog("Error creating PDF", e);
                    }
                });
            };
        }

        void AssignCreateTiffButtonHandler()
        {
            var createTiffButton = FindViewById<Button>(Resource.Id.createTiffButton);
            createTiffButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }
                if (!CheckDocumentImage()) { return; }

                DebugLog("Starting TIFF creation...");

                var tiffOutputUri = GenerateRandomFileUriInExternalStorage(".tiff");

                Task.Run(() =>
                {
                    try
                    {
                        var images = new AndroidNetUri[] { documentImageUri }; // add more images for PDF pages here
                        // The SDK call is sync!
                        SBSDK.WriteTiff(images, tiffOutputUri, new TiffOptions { OneBitEncoded = true });
                        DebugLog("TIFF file created: " + tiffOutputUri);
                        ShowAlertDialog("TIFF file created: " + tiffOutputUri);
                    }
                    catch (Exception e)
                    {
                        ErrorLog("Error creating TIFF", e);
                    }
                });
            };
        }

        void AssignOcrButtonsHandler()
        {
            var fetchOcrBlobsButton = FindViewById<Button>(Resource.Id.fetchOcrBlobsButton);
            fetchOcrBlobsButton.Click += delegate
            {
                FetchOcrBlobFiles();
            };

            performOcrButton = FindViewById<Button>(Resource.Id.performOcrButton);
            performOcrButton.Click += delegate
            {
                if (!CheckScanbotSDKLicense()) { return; }
                if (!CheckDocumentImage()) { return; }
                if (!CheckOcrBlobFiles()) { return; }

                performOcrButton.Post(() => {
                    performOcrButton.Text = "Running OCR ... Please wait ...";
                    performOcrButton.Enabled = false;
                });

                Task.Run(() => {
                    try
                    {
                        var pdfOutputUri = GenerateRandomFileUriInExternalStorage(".pdf");

                        var images = new AndroidNetUri[] { documentImageUri }; // add more images for OCR here
                        var ocrText = PerformOCR(images, pdfOutputUri);
                        DebugLog("Recognized OCR text: " + ocrText);
                        DebugLog("Sandwiched PDF file created: " + pdfOutputUri);
                        OpenPDFFile(pdfOutputUri);
                    }
                    catch (Exception e)
                    {
                        ErrorLog("Error performing OCR", e);
                    }
                    finally
                    {
                        performOcrButton.Post(() => {
                            performOcrButton.Text = "Perform OCR";
                            performOcrButton.Enabled = true;
                        });
                    }
                });
            };
        }

        void AssignMrzScannerButtonHandler()
        {
            var mrzScannerButton = FindViewById<Button>(Resource.Id.mrzScannerButton);
            mrzScannerButton.Click += delegate
            {
                var configuration = new MRZScannerConfiguration();
                // Customize colors, text resources, etc via configuration:
                //configuration.setFinderLineColor(Color.parseColor("#FF0000"));
                //configuration.set...
                var intent = MRZScannerActivity.NewIntent(this, configuration);
                StartActivityForResult(intent, REQUEST_SB_MRZ_SCANNER);
            };
        }

        void AssignBarcodeScannerButtonHandler()
        {
            var barcodeScannerButton = FindViewById<Button>(Resource.Id.barcodeScannerButton);
            barcodeScannerButton.Click += delegate
            {
                var configuration = new BarcodeScannerConfiguration();
                // Customize colors, text resources, etc via configuration:
                //configuration.setFinderLineColor(Color.parseColor("#FF0000"));
                //configuration.set...
                var intent = BarcodeScannerActivity.NewIntent(this, configuration);
                StartActivityForResult(intent, REQUEST_SB_BARCODE_SCANNER);
            };
        }

        bool CheckOcrBlobFiles()
        {
            foreach (var blob in OcrBlobs())
            {
                if (!blobManager.IsBlobAvailable(blob))
                {
                    Toast.MakeText(this, "Please fetch OCR blob files first!", ToastLength.Long).Show();
                    return false;
                }
            }
            return true;
        }

        bool CheckDocumentImage()
        {
            if (documentImageUri == null)
            {
                Toast.MakeText(this, "Please snap a document image via Scanning UI or run Document Detection on an image file from the gallery", ToastLength.Long).Show();
                return false;
            }
            return true;
        }

        bool CheckOriginalImage()
        {
            if (originalImageUri == null)
            {
                Toast.MakeText(this, "Please snap a document image via Scanning UI or run Document Detection on an image file from the gallery", ToastLength.Long).Show();
                return false;
            }
            return true;
        }

        bool CheckScanbotSDKLicense()
        {
            if (SBSDK.IsLicenseValid())
            {
                // Trial period, valid trial license or valid production license.
                return true;
            }

            Toast.MakeText(this, "Scanbot SDK (trial) license has expired!", ToastLength.Long).Show();
            return false;
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == REQUEST_SB_SCANNING_UI && resultCode == Result.Ok)
            {
                documentImageUri = AndroidNetUri.Parse(data.GetStringExtra(CameraViewDemoActivity.EXTRAS_ARG_DOC_IMAGE_FILE_URI));
                originalImageUri = AndroidNetUri.Parse(data.GetStringExtra(CameraViewDemoActivity.EXTRAS_ARG_ORIGINAL_IMAGE_FILE_URI));
                ShowImageView(ImageUtils.LoadImage(documentImageUri, this));
                return;
            }

            if (requestCode == REQUEST_SB_CROPPING_UI && resultCode == Result.Ok)
            {
                documentImageUri = AndroidNetUri.Parse(data.GetStringExtra(CroppingImageDemoActivity.EXTRAS_ARG_IMAGE_FILE_URI));
                ShowImageView(ImageUtils.LoadImage(documentImageUri, this));
                return;
            }

            if (requestCode == REQUEST_SYSTEM_GALLERY && resultCode == Result.Ok)
            {
                originalImageUri = data.Data;
                RunDocumentDetection(originalImageUri);
                return;
            }

            if (requestCode == REQUEST_SB_MRZ_SCANNER && resultCode == Result.Ok)
            {
                var mrzRecognitionResult = data.GetParcelableExtra(MRZScannerActivity.ExtractedFieldsExtra) as MRZRecognitionResult;
                Toast.MakeText(this, ExtractMrzResultData(mrzRecognitionResult), ToastLength.Long).Show();
                return;
            }

            if (requestCode == REQUEST_SB_BARCODE_SCANNER && resultCode == Result.Ok)
            {
                var barcodeResult = data.GetParcelableExtra(BarcodeScannerActivity.ScannedBarcodeExtra) as BarcodeScanningResult;
                Toast.MakeText(this, barcodeResult.BarcodeFormat + "\n" + barcodeResult.Text, ToastLength.Long).Show();
                return;
            }

        }

        string ExtractMrzResultData(MRZRecognitionResult result)
        {
            return new System.Text.StringBuilder()
                    .Append("documentCode: ").Append(result.DocumentCodeField().Value).Append("\n")
                    .Append("First name: ").Append(result.FirstNameField().Value).Append("\n")
                    .Append("Last name: ").Append(result.LastNameField().Value).Append("\n")
                    .Append("issuingStateOrOrganization: ").Append(result.IssuingStateOrOrganizationField().Value).Append("\n")
                    .Append("departmentOfIssuance: ").Append(result.DepartmentOfIssuanceField().Value).Append("\n")
                    .Append("nationality: ").Append(result.NationalityField().Value).Append("\n")
                    .Append("dateOfBirth: ").Append(result.DateOfBirthField().Value).Append("\n")
                    .Append("gender: ").Append(result.GenderField().Value).Append("\n")
                    .Append("dateOfExpiry: ").Append(result.DateOfExpiryField().Value).Append("\n")
                    .Append("personalNumber: ").Append(result.PersonalNumberField().Value).Append("\n")
                    .Append("optional1: ").Append(result.Optional1Field().Value).Append("\n")
                    .Append("optional2: ").Append(result.Optional2Field().Value).Append("\n")
                    .Append("discreetIssuingStateOrOrganization: ").Append(result.DiscreetIssuingStateOrOrganizationField().Value).Append("\n")
                    .Append("validCheckDigitsCount: ").Append(result.ValidCheckDigitsCount).Append("\n")
                    .Append("checkDigitsCount: ").Append(result.CheckDigitsCount).Append("\n")
                    .Append("travelDocType: ").Append(result.TravelDocTypeField().Value).Append("\n")
                    .ToString();
        }

        void RunDocumentDetection(AndroidNetUri imageUri)
        {
            DebugLog("Running document detection on image: " + imageUri);

            Task.Run(() =>
            {
                try
                {
                    // The SDK call is sync!
                    var detectionResult = SBSDK.DetectDocument(imageUri, this);
                    DebugLog("Document detection result: " + detectionResult.Status);
                    if (detectionResult.Status.IsOk())
                    {
                        var documentImage = detectionResult.Image as Bitmap;
                        documentImageUri = TempImageStorage.AddImage(documentImage);
                        ShowImageView(documentImage);

                        DebugLog("Detected polygon: ");
                        foreach (var p in detectionResult.Polygon)
                        {
                            DebugLog(p.ToString());
                        }
                    }
                    else
                    {
                        DebugLog("No document detected!");
                        RunOnUiThread(() =>
                        {
                            Toast.MakeText(this, "No document detected! (Detection result: " + detectionResult.Status + ")", ToastLength.Long).Show();
                        });
                    }
                }
                catch (Exception e)
                {
                    ErrorLog("Error while document detection", e);
                }
            });
        }


        string PerformOCR(AndroidNetUri[] images, AndroidNetUri pdfOutputFileUri = null)
        {
            DebugLog("Performing OCR...");

            var pages = new List<Page>();
            foreach (AndroidNetUri imageUri in images)
            {
                var path = FileChooserUtils.GetPath(this, imageUri);
                var imageFile = new Java.IO.File(path);
                DebugLog("Creating a page of image file: " + imageFile);
                var page = pageFactory.BuildPage(imageFile);
                pages.Add(page);
            }

            if (pdfOutputFileUri == null)
            {
                // Perform OCR only for plain text result:
                var ocrResultWithTextOnly = textRecognition.WithoutPDF(ocrLanguages, pages).Recognize();
                return ocrResultWithTextOnly.RecognizedText;
            }

            // Perform OCR for PDF file with OCR information (sandwiched PDF):
            var document = new Document();
            document.Name = "document.pdf";
            document.OcrStatus = OcrStatus.Pending;
            document.Id = Java.Util.UUID.RandomUUID().ToString();
            var fullOcrResult = textRecognition.WithPDF(ocrLanguages, document, pages).Recognize();

            // move sandwiched PDF result file into requested target:
            Java.IO.File tempPdfFile = null;
            try
            {
                ISharedPreferences preferences = PreferenceManager.GetDefaultSharedPreferences(this);
                DocumentStoreStrategy documentStoreStrategy = new DocumentStoreStrategy(this, preferences);
                tempPdfFile = documentStoreStrategy.GetDocumentFile(fullOcrResult.SandwichedPdfDocument.Id, fullOcrResult.SandwichedPdfDocument.Name);
                DebugLog("Got temp PDF file from SDK: " + tempPdfFile);
                if (tempPdfFile != null && tempPdfFile.Exists())
                {
                    DebugLog("Copying temp file to target output file: " + pdfOutputFileUri);
                    File.Copy(tempPdfFile.AbsolutePath, new Java.IO.File(pdfOutputFileUri.Path).AbsolutePath);
                }
                else
                {
                    ErrorLog("Could not get sandwiched PDF document file from SDK!");
                }
            }
            finally
            {
                if (tempPdfFile != null && tempPdfFile.Exists())
                {
                    DebugLog("Deleting temp file: " + tempPdfFile);
                    tempPdfFile.Delete();
                }
            }

            return fullOcrResult.RecognizedText;
        }

        void ShowImageView(Bitmap bitmap)
        {
            imageView.Post(() =>
            {
                var thumb = ImageUtils.GetThumbnail(bitmap, BIG_THUMB_MAX_W, BIG_THUMB_MAX_H);
                imageView.SetImageBitmap(thumb);
            });
        }

        void OpenPDFFile(AndroidNetUri pdfFileUri)
        {
            Intent openIntent = new Intent();
            openIntent.SetAction(Intent.ActionView);
            openIntent.SetDataAndType(pdfFileUri, "application/pdf");
            openIntent.SetFlags(ActivityFlags.ClearWhenTaskReset | ActivityFlags.NewTask);

            if (openIntent.ResolveActivity(this.PackageManager) != null)
            {
                StartActivity(openIntent);
            }
            else
            {
                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, "Error opening PDF document", ToastLength.Long).Show();
                });
            }
        }

        string GetPublicExternalStorageDirectory()
        {
            var externalPublicPath = System.IO.Path.Combine(
                AndroidOS.Environment.ExternalStorageDirectory.Path, "scanbot-sdk-example-xamarin");
            Directory.CreateDirectory(externalPublicPath);
            return externalPublicPath;
        }

        AndroidNetUri GenerateRandomFileUriInExternalStorage(string fileExtension)
        {
            var externalPath = GetPublicExternalStorageDirectory();
            var targetFile = System.IO.Path.Combine(externalPath, UUID.RandomUUID() + fileExtension);
            return AndroidNetUri.FromFile(new Java.IO.File(targetFile));
        }

        void ShowAlertDialog(string message, string title = "Info")
        {
            RunOnUiThread(() =>
            {
                AlertDialog.Builder builder = new AlertDialog.Builder(this);
                builder.SetTitle(title);
                builder.SetMessage(message);
                var alert = builder.Create();
                alert.SetButton("OK", (c, ev) =>
                {
                    alert.Dismiss();
                });
                alert.Show();
            });
        }

        void DebugLog(string msg)
        {
            Log.Debug(LOG_TAG, msg);
        }

        void ErrorLog(string msg)
        {
            Log.Error(LOG_TAG, msg);
        }

        void ErrorLog(string msg, Exception ex)
        {
            Log.Error(LOG_TAG, Java.Lang.Throwable.FromException(ex), msg);
        }

    }

    class ImageFilterDialog : DialogFragment
    {
        static List<string> ImageFilterItems = new List<string>();

        static ImageFilterDialog()
        {
            ImageFilterItems.Add(ImageFilter.Binarized.ToString());
            ImageFilterItems.Add(ImageFilter.Grayscale.ToString());
            ImageFilterItems.Add(ImageFilter.ColorEnhanced.ToString());
            ImageFilterItems.Add(ImageFilter.ColorDocument.ToString());
            ImageFilterItems.Add(ImageFilter.PureBinarized.ToString());
            ImageFilterItems.Add(ImageFilter.BackgroundClean.ToString());
            ImageFilterItems.Add(ImageFilter.BlackAndWhite.ToString());
        }

        Action<ImageFilter> ApplyFilterAction;

        internal ImageFilterDialog(Action<ImageFilter> applyFilterAction)
        {
            ApplyFilterAction = applyFilterAction;
        }
            
        public override Dialog OnCreateDialog(Bundle savedInstanceState)
        {
            AlertDialog.Builder builder = new AlertDialog.Builder(Activity);
            builder.SetTitle("Pick an Image Filter");
            builder.SetItems(ImageFilterItems.ToArray(), (sender, args) => {
                var filterName = ImageFilterItems[args.Which];
                var filter = (ImageFilter)Enum.Parse(typeof(ImageFilter), filterName);
                ApplyFilterAction?.Invoke(filter);
            });

            return builder.Create();
        }
    }
}

