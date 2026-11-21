using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntsLiveCoach
{
    public sealed class ReferenceLibraryEntry
    {
        public readonly string Path, Title, Game, Car, Track;
        public string CourseLabel { get; private set; }
        public readonly bool IsCommunityRecording;
        public ReferenceLibraryEntry(string path,ReferenceLap lap)
        {
            Path=System.IO.Path.GetFullPath(path);Game=lap.Game;Car=lap.Car;Track=lap.Track;
            IsCommunityRecording=ReferenceComparison.NeedsNativePositionCheck(lap);
            string model;
            string car=lap.Conditions.TryGetValue("sourceCarModel",out model) && !string.IsNullOrWhiteSpace(model)
                ? model+" / "+lap.Car : lap.Car;
            string layout=ReferenceLap.Unknown(lap.Layout)?"":" ("+lap.Layout+")";
            CourseLabel=(string.IsNullOrWhiteSpace(model)?lap.Car:model)+" · "+lap.Track+
                (layout.Length==0?" (세부 레이아웃 미확인)":layout)+" · "+lap.Author;
            Title=lap.Game+" · "+car+" · "+lap.Track+layout+" · "+lap.Author+
                (ReferenceLibrary.ViewOnly(lap)?" [입력 열람 전용]":!lap.HasTime?" [위치·입력 기준 / 시각 없음]":"");
        }
        public override string ToString() {return Title;}
    }
    public sealed class ReferenceLibraryResult
    {
        public readonly ReferenceLibraryEntry[] Entries;
        public readonly string[] Errors;
        public ReferenceLibraryResult(ReferenceLibraryEntry[] entries,string[] errors) {Entries=entries;Errors=errors;}
    }
    public static class ReferenceLibrary
    {
        public static bool ViewOnly(ReferenceLap lap)
        {return lap!=null&&lap.DistanceBasis=="acc-integrated-travel-distance-m";}
        public const int MaximumFiles=64;
        public const long MaximumScanBytes=64L*1024*1024;
        public static ReferenceLap Read(string path)
        {
            if(new FileInfo(path).Length>ReferenceLap.MaximumJsonCharacters)throw new FormatException("파일이 32 MiB 제한보다 큽니다.");
            return ReferenceLap.LoadJson(File.ReadAllText(path));
        }
        public static ReferenceLibraryResult Scan(string directory)
        {
            var entries=new List<ReferenceLibraryEntry>();var errors=new List<string>();
            if(!Directory.Exists(directory))return new ReferenceLibraryResult(entries.ToArray(),errors.ToArray());
            string[] files;
            try {files=Directory.GetFiles(directory,"*.json",SearchOption.TopDirectoryOnly).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();}
            catch(IOException ex){return new ReferenceLibraryResult(entries.ToArray(),new[]{ex.Message});}
            catch(UnauthorizedAccessException ex){return new ReferenceLibraryResult(entries.ToArray(),new[]{ex.Message});}
            long total=0;
            if(files.Length>MaximumFiles)errors.Add("한 번에 64개 기준 파일까지 표시합니다.");
            foreach(string path in files.Take(MaximumFiles))
            {
                try
                {
                    long size=new FileInfo(path).Length;
                    if(size>ReferenceLap.MaximumJsonCharacters){errors.Add(System.IO.Path.GetFileName(path)+": 파일 크기 초과");continue;}
                    if(total+size>MaximumScanBytes){errors.Add("라이브러리 탐색 64 MiB 한도에 도달했습니다. 파일 열기로 개별 선택하세요.");break;}
                    total+=size;var lap=Read(path);entries.Add(new ReferenceLibraryEntry(path,lap));
                }
                catch(Exception ex)
                {
                    if(!(ex is IOException)&&!(ex is UnauthorizedAccessException)&&!(ex is FormatException)&&!(ex is ArgumentException))throw;
                    errors.Add(System.IO.Path.GetFileName(path)+": "+ex.Message);
                }
            }
            return new ReferenceLibraryResult(entries.ToArray(),errors.ToArray());
        }
    }
}
